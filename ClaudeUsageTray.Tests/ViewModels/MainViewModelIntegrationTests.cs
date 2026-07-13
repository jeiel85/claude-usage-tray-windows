using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class MainViewModelIntegrationTests
{
    private static MainViewModel CreateViewModel()
    {
        if (System.Windows.Application.Current == null)
            new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        var settingsService = new SettingsService();
        var credService = new CredentialService();
        var apiService = new UsageApiService(credService);
        var sessionMonitor = new SessionMonitor();
        var codexMonitor = new CodexUsageMonitor();
        var geminiCliMonitor = new GeminiCliUsageMonitor();
        var openCodeMonitor = new OpenCodeUsageMonitor();
        var antigravityMonitor = new AntigravityUsageMonitor();
        var notifier = new NotificationService(() => null);
        var updater = new UpdateService();
        var history = new HistoryService();
        var usageSync = new UsageSyncService();
        var weather = new WeatherService();
        var weatherAlert = new WeatherAlertService(
            weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>());

        return new MainViewModel(apiService, credService, sessionMonitor, codexMonitor, geminiCliMonitor,
            openCodeMonitor, antigravityMonitor, notifier, settingsService, updater, history, usageSync, weather, weatherAlert);
    }

    [Fact]
    public async Task MainViewModel_ConstructsSuccessfully()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Assert.NotNull(vm);
                Assert.NotNull(vm.AntigravityVm);
                Assert.NotNull(vm.WeatherVm);
                Assert.NotNull(vm.OpenCodeVm);
                Assert.NotNull(vm.GeminiVm);
                Assert.NotNull(vm.CodexVm);
                Assert.NotNull(vm.ClaudeVm);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task RefreshAsync_CompletesWithoutException()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = CreateViewModel();
            try
            {
                var exception = await Record.ExceptionAsync(() => vm.RefreshAsync());
                Assert.Null(exception);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task RefreshAsync_SetsLastUpdatedLabel()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            var vm = CreateViewModel();
            try
            {
                await vm.RefreshAsync();
                Assert.False(string.IsNullOrWhiteSpace(vm.LastUpdatedLabel));
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task UsagePopup_MiniMode_UpdatesOpacityAndCollapsesChrome()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                vm.KeepPopupAboveTaskbar = true;
                vm.UsagePanelOpacity = 0.82;
                using var popup = new UsagePopup(vm);

                popup.ShowNearTray();

                Assert.Equal(0.82, popup.Opacity, 2);

                var historySection = Assert.IsType<System.Windows.Controls.StackPanel>(popup.FindName("ClaudeHistorySection"));
                var footerSection = Assert.IsType<System.Windows.Controls.Border>(popup.FindName("PopupFooter"));
                Assert.Equal(System.Windows.Visibility.Collapsed, historySection.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, footerSection.Visibility);

                vm.KeepPopupAboveTaskbar = false;

                Assert.Equal(1.0, popup.Opacity, 2);
                Assert.Equal(System.Windows.Visibility.Visible, historySection.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, footerSection.Visibility);

                popup.Hide();
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task SaveSettings_DoesNotThrow()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                var exception = Record.Exception(() => vm.SaveSettingsCommand.Execute(null));
                Assert.Null(exception);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task Dispose_DoesNotThrow()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            var exception = Record.Exception(() => vm.Dispose());
            Assert.Null(exception);
        });
    }
}
