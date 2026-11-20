using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;
using Application = System.Windows.Application;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ClaudeUsageTray.Tests.ViewModels;

// v1.50.0 은 라벨·남은 횟수·초기화 문구를 한 줄에 둬서, 절대 시각까지 붙으면 라벨이 "Ch…" 로 잘렸다(#210).
// 실제 팝업을 화면 밖에 띄워 배치한 뒤, 라벨이 잘리지 않고 초기화 문구가 아랫줄로 내려갔는지 본다.
// Loc 언어를 바꾸므로 Loc 을 건드리는 다른 테스트와 같은 컬렉션에서 직렬로 돈다.
[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class CodexImageGenerationRowLayoutTests : IDisposable
{
    private readonly string _dir;

    public CodexImageGenerationRowLayoutTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cut-imagegen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    // MainViewModelIntegrationTests.CreateViewModel 과 같은 격리 구성 — 개발자 PC 의 설정·자격 파일을 건드리지 않는다.
    private MainViewModel CreateViewModel()
    {
        if (Application.Current == null)
            new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        var credService = new CredentialService(Path.Combine(_dir, ".credentials.json"));
        var notifier = new NotificationService(() => null);
        var weather = new WeatherService();
        return new MainViewModel(new UsageApiService(credService), credService, new SessionMonitor(),
            new CodexUsageMonitor(), new OpenCodeUsageMonitor(), new AntigravityUsageMonitor(), notifier,
            new SettingsService(Path.Combine(_dir, "claude-usage-tray.json")), new UpdateService(),
            new HistoryService(_dir), new UsageSyncService(Path.Combine(_dir, "appdata")), weather,
            new WeatherAlertService(weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>()));
    }

    [Theory]
    [InlineData("ko")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("en")]
    public async Task ImageGenerationRow_KeepsLabelWholeAndPutsResetOnItsOwnLine(string lang)
    {
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage(lang);
                vm.IsCodexActive = true;
                vm.FocusedProvider = UsageProviderKind.Codex;
                // 가장 긴 경우: 절대 시각까지 붙은 초기화 문구
                vm.ShowAbsoluteResetTime = true;
                var now = DateTimeOffset.Now;
                vm.CodexVm.ApplyImageGeneration(new FeatureAllowance(120, now.AddHours(23).AddMinutes(58)), now, true);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                var row = Assert.IsType<StackPanel>(popup.FindName("CodexImageGenerationRow"));
                var label = Assert.IsType<TextBlock>(popup.FindName("CodexImageGenerationLabel"));
                var reset = Assert.IsType<TextBlock>(popup.FindName("CodexImageGenerationReset"));
                Assert.Equal(Visibility.Visible, row.Visibility);
                Assert.Equal(Loc.ChatGptImageGeneration, label.Text);
                Assert.Equal(Visibility.Visible, reset.Visibility);
                Assert.Contains("(", reset.Text);

                // 라벨이 잘리지 않았다 = 배치된 폭이 제약 없이 잰 폭 이상이다.
                Assert.True(label.ActualWidth + 0.5 >= UnconstrainedWidth(label),
                    $"label '{label.Text}' trimmed: {label.ActualWidth:F1} < {UnconstrainedWidth(label):F1}");
                // 초기화 문구는 라벨 아래 줄에 있다.
                var labelBottom = label.TranslatePoint(new Point(0, label.ActualHeight), row).Y;
                var resetTop = reset.TranslatePoint(new Point(0, 0), row).Y;
                Assert.True(resetTop >= labelBottom, $"reset line overlaps label row: {resetTop:F1} < {labelBottom:F1}");
                Assert.True(reset.ActualWidth + 0.5 >= UnconstrainedWidth(reset),
                    $"reset '{reset.Text}' trimmed: {reset.ActualWidth:F1} < {UnconstrainedWidth(reset):F1}");

                popup.Hide();
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task ImageGenerationRow_HidesResetLineWhenResetTimeIsUnknown()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                vm.IsCodexActive = true;
                vm.FocusedProvider = UsageProviderKind.Codex;
                vm.CodexVm.ApplyImageGeneration(new FeatureAllowance(5, null), DateTimeOffset.Now, false);

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                Assert.Equal(Visibility.Visible, Assert.IsType<StackPanel>(popup.FindName("CodexImageGenerationRow")).Visibility);
                Assert.Equal(Visibility.Collapsed, Assert.IsType<TextBlock>(popup.FindName("CodexImageGenerationReset")).Visibility);

                popup.Hide();
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    private static double UnconstrainedWidth(TextBlock source)
    {
        var probe = new TextBlock
        {
            Text = source.Text,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontWeight = source.FontWeight,
            FontStyle = source.FontStyle,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }
}
