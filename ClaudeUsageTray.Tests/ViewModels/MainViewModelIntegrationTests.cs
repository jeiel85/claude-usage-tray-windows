using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.ViewModels;
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
        var weather = new WeatherService();
        var weatherAlert = new WeatherAlertService(
            weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>());

        return new MainViewModel(apiService, credService, sessionMonitor, codexMonitor, geminiCliMonitor,
            openCodeMonitor, antigravityMonitor, notifier, settingsService, updater, history, weather, weatherAlert);
    }

    [Fact]
    public void MainViewModel_ConstructsSuccessfully()
    {
        var vm = CreateViewModel();
        Assert.NotNull(vm);
        Assert.NotNull(vm.AntigravityVm);
        Assert.NotNull(vm.WeatherVm);
        Assert.NotNull(vm.OpenCodeVm);
        Assert.NotNull(vm.GeminiVm);
        Assert.NotNull(vm.CodexVm);
        Assert.NotNull(vm.ClaudeVm);
    }

    [Fact]
    public async Task RefreshAsync_CompletesWithoutException()
    {
        var vm = CreateViewModel();
        var exception = await Record.ExceptionAsync(() => vm.RefreshAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task RefreshAsync_SetsLastUpdatedLabel()
    {
        var vm = CreateViewModel();
        await vm.RefreshAsync();
        Assert.False(string.IsNullOrWhiteSpace(vm.LastUpdatedLabel));
    }

    [Fact]
    public void SaveSettings_DoesNotThrow()
    {
        var vm = CreateViewModel();
        var exception = Record.Exception(() => vm.SaveSettingsCommand.Execute(null));
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var vm = CreateViewModel();
        var exception = Record.Exception(() => vm.Dispose());
        Assert.Null(exception);
    }
}
