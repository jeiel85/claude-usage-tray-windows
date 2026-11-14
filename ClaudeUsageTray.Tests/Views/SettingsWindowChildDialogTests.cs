using System.IO;
using System.Reflection;
using System.Windows;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.Tests.ViewModels;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>
/// 설정 창은 바깥을 누르면(비활성화되면) 숨는다. 하지만 자기 소유의 자식 창(QR 연결 창 등)으로 포커스가 넘어갈 때
/// 숨으면, 메인 팝업이 다시 앞으로 올라와 그 자식 창을 덮는다 (#232).
/// </summary>
[Collection("WpfTests")]
public class SettingsWindowChildDialogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cut-settings-child-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Deactivation_KeepsSettingsVisible_WhileAnOwnedWindowIsOpen_AndHidesItOtherwise()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? settings = null;
            Window? child = null;
            var childClosed = false;
            try
            {
                settings = new SettingsWindow(vm) { Left = -10000, Top = -10000 };
                settings.Show();

                child = new Window { Owner = settings, Left = -10000, Top = -10000, ShowActivated = false };
                child.Show();
                RaiseDeactivated(settings);
                Assert.True(settings.IsVisible, "자식 창으로 포커스가 넘어간 것은 바깥 클릭이 아니다 — 설정 창이 숨으면 안 된다.");

                child.Close();
                childClosed = true;
                RaiseDeactivated(settings);
                Assert.False(settings.IsVisible, "자식 창이 없을 때 비활성화되면 지금처럼 숨어야 한다.");
            }
            finally
            {
                // 단언이 실패해도 창을 닫고, SettingsWindow 가 정적 Loc.LanguageChanged 에 남지 않게 Dispose 한다.
                if (!childClosed) child?.Close();
                settings?.Close();
                settings?.Dispose();
                vm.Dispose();
            }
        });
    }

    // 실제 포커스 이동은 테스트 프로세스가 전경이 아니면 일어나지 않으므로, WPF 가 WM_ACTIVATE 에서 부르는 경로를 직접 탄다.
    private static void RaiseDeactivated(Window window) =>
        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [EventArgs.Empty]);

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
