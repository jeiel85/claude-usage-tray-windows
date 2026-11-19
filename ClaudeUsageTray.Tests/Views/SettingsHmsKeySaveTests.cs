using System.IO;
using System.Windows;
using WButton = System.Windows.Controls.Button;
using WCheckBox = System.Windows.Controls.CheckBox;
using WPasswordBox = System.Windows.Controls.PasswordBox;
using WButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.Tests.ViewModels;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>
/// DeepSeek(100milionseek) 키는 [저장]·Enter 로만 저장한다(#311). 리뷰 지적(#312): 키를 입력만 해 둔 채
/// 다른 설정을 바꾸면 그 공용 저장 경로가 입력 중인 키까지 저장·조회했다.
/// </summary>
[Collection("WpfTests")]
public class SettingsHmsKeySaveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cut-settings-hms-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task PendingKey_IsSavedOnlyByTheSaveButton()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? settings = null;
            try
            {
                settings = new SettingsWindow(vm) { Left = -10000, Top = -10000, ShowActivated = false };
                settings.Show();
                var keyBox = (WPasswordBox)settings.FindName("PwdHmsKey");
                var save = (WButton)settings.FindName("BtnHmsKeySave");
                var hideInactive = (WCheckBox)settings.FindName("ChkHideInactive");

                Assert.False(save.IsEnabled);
                keyBox.Password = "hms_pending_key";
                Assert.True(save.IsEnabled);

                // 다른 설정을 바꿔도 입력 중인 키는 저장되지 않고 칸에 남는다.
                hideInactive.IsChecked = hideInactive.IsChecked != true;
                hideInactive.RaiseEvent(new RoutedEventArgs(WButtonBase.ClickEvent));
                Assert.Equal("", vm.HmsApiKeyProtected);
                Assert.Equal("hms_pending_key", keyBox.Password);

                save.RaiseEvent(new RoutedEventArgs(WButtonBase.ClickEvent));
                Assert.Equal("hms_pending_key", ClaudeUsageTray.Services.Mobile.MobileSecret.Unprotect(vm.HmsApiKeyProtected));
                Assert.Equal("", keyBox.Password);
                Assert.False(save.IsEnabled);
            }
            finally
            {
                settings?.Close();
                settings?.Dispose();
                vm.Dispose();
            }
        });
    }

    // #316: DeepSeek 키 칸이 들어가며 트레이 탭 내용이 창 상한을 넘겨, 스크롤이 없던 그 탭의 아래쪽 설정이 잘렸다.
    // 탭 내용은 항목이 늘어도 잘리지 않게 모두 ScrollViewer 안에 둔다(docs/UI_GUIDELINES.md 5).
    [Theory]
    [InlineData("TabGeneral")]
    [InlineData("TabTray")]
    [InlineData("TabAlerts")]
    [InlineData("TabConnect")]
    [InlineData("TabWeather")]
    public async Task EveryTab_ScrollsInsteadOfClipping(string tabName)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? settings = null;
            try
            {
                settings = new SettingsWindow(vm);
                var tab = Assert.IsType<System.Windows.Controls.TabItem>(settings.FindName(tabName));
                var card = Assert.IsType<System.Windows.Controls.Border>(tab.Content);
                Assert.IsType<System.Windows.Controls.ScrollViewer>(card.Child);
            }
            finally
            {
                settings?.Close();
                settings?.Dispose();
                vm.Dispose();
            }
        });
    }

    private MainViewModel CreateViewModel()
    {
        Directory.CreateDirectory(_dir);
        var settingsService = new SettingsService(Path.Combine(_dir, "claude-usage-tray.json"));
        var credService = new CredentialService(Path.Combine(_dir, ".credentials.json"));
        var notifier = new NotificationService(() => null);
        var weather = new WeatherService();
        var weatherAlert = new WeatherAlertService(
            weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>());
        return new MainViewModel(new UsageApiService(credService), credService, new SessionMonitor(),
            new CodexUsageMonitor(), new OpenCodeUsageMonitor(), new AntigravityUsageMonitor(), notifier,
            settingsService, new UpdateService(), new HistoryService(_dir),
            new UsageSyncService(Path.Combine(_dir, "appdata")), weather, weatherAlert,
            claudeAccount: new ClaudeAccountProfileReader(Path.Combine(_dir, ".claude.json")));
    }
}
