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
/// #333: 동기화 폴더 [찾아보기]의 WinForms 대화상자는 OwnedWindows 에 잡히지 않아, 그것이 뜨며 설정 창이 비활성화되면
/// "바깥 클릭" 으로 보고 설정 창이 숨었고 팝업이 그 위로 다시 떠 대화상자를 가렸다. 대화상자는 설정 창을 주인으로 받고,
/// 떠 있는 동안에는 비활성화돼도 숨지 않는다. 대화상자 자체는 모달이라 띄우지 않고, 띄우는 함수 안에서 비활성화를 흉내 낸다.
/// </summary>
[Collection("WpfTests")]
public class SettingsNativeDialogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cut-settings-dialog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static readonly MethodInfo OnDeactivated =
        typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public async Task WhileAWinFormsDialogIsOpen_TheSettingsWindowStaysAndOwnsIt()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? settings = null;
            try
            {
                settings = new SettingsWindow(vm) { Left = -10000, Top = -10000, ShowActivated = false };
                settings.Show();
                var handle = new System.Windows.Interop.WindowInteropHelper(settings).Handle;

                var owner = settings.WithNativeDialog(o =>
                {
                    OnDeactivated.Invoke(settings, [EventArgs.Empty]); // 대화상자가 떠서 포커스를 가져갔다
                    Assert.True(settings.IsVisible);
                    return o.Handle;
                });
                Assert.Equal(handle, owner);

                // 대화상자가 닫힌 뒤의 바깥 클릭은 예전처럼 숨긴다.
                OnDeactivated.Invoke(settings, [EventArgs.Empty]);
                Assert.False(settings.IsVisible);
            }
            finally
            {
                settings?.Close();
                settings?.Dispose();
                vm.Dispose();
            }
        });
    }

    // #334 리뷰: 날씨 기준값 칸에 "NaN"·"1e999" 를 넣으면 저장이 ArgumentException 으로 앱을 끝냈다. 칸이 받지 않는다.
    [Theory]
    [InlineData("NaN", false)]
    [InlineData("1e999", false)]
    [InlineData("-1e999", false)]
    [InlineData("abc", false)]
    [InlineData("33", true)]
    [InlineData("-10", true)]
    public void WeatherThresholdField_AcceptsOnlyFiniteNumbers(string text, bool accepted)
    {
        Assert.Equal(accepted, SettingsWindow.TryParseFinite(text, out var value));
        if (accepted) Assert.True(double.IsFinite(value));
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
