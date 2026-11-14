using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.Tests.ViewModels;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>
/// 창을 이미지로 렌더링해 UI 변경을 눈으로 확인한다(#224). 개발 환경(리눅스 컨테이너)에서는 WPF 를 띄울 수 없어
/// Windows CI 에서만 의미가 있다. <c>CUT_UI_SNAPSHOT_DIR</c> 가 없으면 아무것도 하지 않는다 — 일반 테스트 실행을 느리게
/// 하거나 개발자 PC 에 파일을 남기지 않게.
/// </summary>
[Collection("WpfTests")]
[Trait("Category", "UiSnapshot")]
public class UiSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cut-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string? OutputDir => Environment.GetEnvironmentVariable("CUT_UI_SNAPSHOT_DIR");

    [Fact]
    public async Task RenderMainWindows()
    {
        if (string.IsNullOrEmpty(OutputDir)) return;
        Directory.CreateDirectory(OutputDir);

        await WpfTestHost.RunAsync(() =>
        {
            var lang = Environment.GetEnvironmentVariable("CUT_UI_SNAPSHOT_LANG") ?? "ko";
            var vm = CreateViewModel();
            try
            {
                // 설정을 읽으며 언어가 "시스템"(CI 는 영어)으로 돌아가므로 뷰모델을 만든 뒤에 정한다.
                vm.SelectedLanguage = lang;
                Loc.SetLanguage(lang);
                FillSampleData(vm);

                var popup = new UsagePopup(vm);
                Render(popup, "popup");
                vm.FocusedProvider = UsageProviderKind.Codex;
                Render(popup, "popup-codex");
                vm.FocusedProvider = UsageProviderKind.Claude;
                popup.Close();

                var settings = new SettingsWindow(vm);
                settings.Show();
                settings.UpdateLayout();
                var tabs = settings.FindName("SettingsTabs") as System.Windows.Controls.TabControl
                    ?? FindChild<System.Windows.Controls.TabControl>(settings);
                if (tabs is null)
                {
                    Render(settings, "settings");
                }
                else
                {
                    for (var i = 0; i < tabs.Items.Count; i++)
                    {
                        tabs.SelectedIndex = i;
                        Render(settings, $"settings-{i}");
                    }
                }
                settings.Close();

                var connect = new MobileConnectWindow(vm.Mobile);
                Render(connect, "mobile-connect");
                connect.Close();
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// 화면 밖에 띄워 레이아웃을 끝낸 뒤 내용물(그림자 여백 포함 루트)을 JPEG 로 저장한다.
    /// 로그로 내보내므로(아티팩트를 받을 수 없는 환경) 크기를 줄이려고 JPEG 를 쓴다.
    /// </summary>
    private static void Render(Window window, string name)
    {
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = 0;
        window.Show();
        window.UpdateLayout();
        // 늦게 붙는 바인딩·레이아웃(Loaded 뒤 처리)을 끝내려고 잠깐 디스패처를 돌린다.
        var until = DateTime.UtcNow.AddMilliseconds(400);
        while (DateTime.UtcNow < until) DoEvents();

        if (window.Content is not FrameworkElement root) return;
        // 1.5배(144 DPI)로 그려 작은 글씨도 읽히게 한다.
        const double scale = 1.5;
        var width = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x30, 0x38)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(visual);
        var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(OutputDir!, name + ".jpg"));
        encoder.Save(file);
        window.Hide();
    }

    private static void DoEvents()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    /// <summary>실제 계정 없이도 화면 구성을 볼 수 있게 대표적인 값을 채운다.</summary>
    private static void FillSampleData(MainViewModel vm)
    {
        var c = vm.ClaudeVm;
        c.HasQuotaData = true;
        c.ShortPercent = 0.42;
        c.LongPercent = 0.18;
        c.ShortReset = "2h 13m";
        c.LongReset = "3d 4h";
        c.ShortTimePercent = 0.55;
        c.HasShortTimeline = true;
        c.ShortUsageCapped = 0.42;
        c.LongTimePercent = 0.6;
        c.HasLongTimeline = true;
        c.LongUsageCapped = 0.18;
        c.IsActive = true;
        c.ShortSummary = "42% · 2h 13m";
        c.LongSummary = "18% · 3d 4h";
        vm.HideInactiveProviders = false;
        vm.FocusedProvider = UsageProviderKind.Claude;
        vm.TodayInputTokens = 120_000;
        vm.TodayOutputTokens = 34_000;
        vm.TodayCacheRead = 2_500_000;
        vm.TodayCacheWrite = 80_000;
        vm.SessionsLabel = "3";
        vm.TodayCostLabel = "$4.21";
        vm.IsCodexActive = true;
        vm.CodexPercent = 0.37;
        vm.CodexReset = "4h 2m";
        vm.CodexLongPercent = 0.6;
        vm.CodexLongReset = "5d";
        vm.IsCodexLongVisible = true;
    }

    private MainViewModel CreateViewModel()
    {
        Directory.CreateDirectory(_dir);
        var settingsService = new SettingsService(Path.Combine(_dir, "claude-usage-tray.json"));
        var credService = new CredentialService(Path.Combine(_dir, ".credentials.json"));
        var apiService = new UsageApiService(credService);
        var notifier = new NotificationService(() => null);
        var weather = new WeatherService();
        var weatherAlert = new WeatherAlertService(
            weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>());
        return new MainViewModel(apiService, credService, new SessionMonitor(), new CodexUsageMonitor(),
            new OpenCodeUsageMonitor(), new AntigravityUsageMonitor(), notifier, settingsService, new UpdateService(),
            new HistoryService(_dir), new UsageSyncService(Path.Combine(_dir, "appdata")), weather, weatherAlert,
            claudeAccount: new ClaudeAccountProfileReader(Path.Combine(_dir, ".claude.json")));
    }
}
