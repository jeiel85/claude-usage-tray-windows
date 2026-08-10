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

                var shell = Assert.IsType<System.Windows.Controls.Border>(popup.FindName("PopupShell"));
                var frame = Assert.IsType<System.Windows.Controls.Border>(popup.FindName("PopupFrame"));
                var headerSection = Assert.IsType<System.Windows.Controls.Border>(popup.FindName("PopupHeader"));
                var miniSection = Assert.IsType<System.Windows.Controls.StackPanel>(popup.FindName("MiniUsageContent"));
                var fullSection = Assert.IsType<System.Windows.Controls.ScrollViewer>(popup.FindName("FullUsageContent"));
                var historySection = Assert.IsType<System.Windows.Controls.StackPanel>(popup.FindName("ClaudeHistorySection"));
                var footerSection = Assert.IsType<System.Windows.Controls.Border>(popup.FindName("PopupFooter"));
                Assert.Equal(new System.Windows.Thickness(0), shell.Margin);
                Assert.Null(shell.Effect);
                Assert.Equal(new System.Windows.Thickness(0), frame.BorderThickness);
                Assert.Equal(System.Windows.Visibility.Collapsed, headerSection.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, miniSection.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, fullSection.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, historySection.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, footerSection.Visibility);

                vm.KeepPopupAboveTaskbar = false;

                Assert.Equal(1.0, popup.Opacity, 2);
                Assert.Equal(new System.Windows.Thickness(8), shell.Margin);
                Assert.NotNull(shell.Effect);
                Assert.Equal(new System.Windows.Thickness(1), frame.BorderThickness);
                Assert.Equal(System.Windows.Visibility.Visible, headerSection.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, miniSection.Visibility);
                Assert.Equal(System.Windows.Visibility.Visible, fullSection.Visibility);
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
    public async Task UsagePopup_OpenCodeQuotaUsesTheSameFlatGaugeStructureAsOtherProviders()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                using var popup = new UsagePopup(vm);

                var gauges = Assert.IsType<System.Windows.Controls.StackPanel>(
                    popup.FindName("OpenCodeQuotaGauges"));
                Assert.Equal(3, gauges.Children.Count);
                Assert.All(gauges.Children.Cast<System.Windows.UIElement>(), child =>
                    Assert.IsType<System.Windows.Controls.StackPanel>(child));
                Assert.NotNull(popup.FindName("OpenCodeRollingGauge"));
                Assert.NotNull(popup.FindName("OpenCodeWeeklyGauge"));
                Assert.NotNull(popup.FindName("OpenCodeMonthlyGauge"));
                Assert.NotNull(popup.FindName("OpenCodeRollingTimelineMarker"));
                Assert.NotNull(popup.FindName("OpenCodeWeeklyTimelineMarker"));
                Assert.NotNull(popup.FindName("OpenCodeMonthlyTimelineMarker"));
                Assert.NotNull(popup.FindName("OpenCodeStatsSeparator"));
                Assert.NotNull(popup.FindName("OpenCodeTokenTiles"));
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task SettingsWindow_ConstructsWithoutOpacitySliderException()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                vm.KeepPopupAboveTaskbar = true;
                vm.UsagePanelOpacity = 0.82;

                var exception = Record.Exception(() => new SettingsWindow(vm));

                Assert.Null(exception);
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

    // 설정창은 SizeToContent="Height" + MaxHeight 라, 내용이 상한을 넘으면 스크롤이 없는 한
    // 마지막 항목이 조용히 잘린다. 항목을 추가할 때 "들어가거나, 최소한 스크롤은 되거나" 를 보장한다.
    [Fact]
    public async Task SettingsWindow_GeneralTabContentStaysReachable()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? window = null;
            try
            {
                window = new SettingsWindow(vm);
                window.Left = -10000;   // 테스트 중 화면에 뜨지 않도록
                window.Show();
                window.UpdateLayout();

                var content = (System.Windows.FrameworkElement)window.Content;
                // 상한에 눌린 ActualHeight 가 아니라 "원래 필요한" 높이를 잰다.
                content.Measure(new System.Windows.Size(window.Width, double.PositiveInfinity));
                var required = content.DesiredSize.Height;

                var scroller = FindChild<System.Windows.Controls.ScrollViewer>(window);
                bool fitsOutright = required <= window.MaxHeight;
                bool scrollable   = scroller is { ViewportHeight: > 0 };

                Assert.True(fitsOutright || scrollable,
                    $"설정창이 {required:F0}px 를 필요로 하는데 상한은 {window.MaxHeight:F0}px 이고 스크롤도 없다 — 항목이 잘린다.");

                // 스크롤로 해결한 경우, 넘치는 만큼 실제로 스크롤할 수 있어야 한다.
                if (!fitsOutright)
                    Assert.True(scroller!.ScrollableHeight > 0,
                        "내용이 넘치는데 스크롤 가능한 높이가 0 이다 — 넘친 부분에 닿을 수 없다.");
            }
            finally
            {
                window?.Close();
                vm.Dispose();
            }
        });
    }

    // 자동 설치를 끄면 대기 시간 슬라이더는 의미가 없으므로 함께 비활성화된다.
    // (ElementName 바인딩은 깨져도 예외 없이 조용히 false 로 남으므로 실제 상태로 확인한다.)
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingsWindow_CountdownSlider_FollowsAutoUpdateToggle(bool enabled)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? window = null;
            try
            {
                vm.AutoUpdateEnabled = enabled;
                vm.AutoUpdateCountdownSeconds = 45;

                window = new SettingsWindow(vm);
                window.Left = -10000;
                window.Show();
                window.UpdateLayout();

                Assert.Equal(enabled, window.ChkAutoUpdateEnabled.IsChecked);
                Assert.Equal(45, (int)window.SliderAutoUpdateCountdown.Value);
                Assert.Equal(enabled, window.SliderAutoUpdateCountdown.IsEnabled);
            }
            finally
            {
                window?.Close();
                vm.Dispose();
            }
        });
    }

    private static T? FindChild<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            var nested = FindChild<T>(child);
            if (nested != null) return nested;
        }
        return null;
    }
}
