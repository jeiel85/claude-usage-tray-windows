using System.IO;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.WeatherWarnings;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class MainViewModelIntegrationTests : IDisposable
{
    // ~/.claude 대신 쓰는 테스트별 폴더(#186). 실제 서비스를 그대로 쓰면 설정 저장·RefreshAsync 가
    // 개발자 PC 의 설정·히스토리를 다시 쓰고, 만료된 Claude 토큰을 갱신해 자격 파일까지 덮어쓴다.
    // 실제 설정에 다중 PC 동기화가 켜져 있으면 공유 폴더에 이 PC 의 스냅샷도 남긴다.
    private readonly string _dir;

    public MainViewModelIntegrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cut-mainvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private MainViewModel CreateViewModel()
    {
        if (System.Windows.Application.Current == null)
            new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };

        var settingsService = new SettingsService(Path.Combine(_dir, "claude-usage-tray.json"));
        // 없는 자격 파일 — 로그인 전 PC 와 같은 상태(CI 와 같다)로 돌고, 토큰 갱신·저장이 일어나지 않는다.
        var credService = new CredentialService(Path.Combine(_dir, ".credentials.json"));
        var apiService = new UsageApiService(credService);
        var sessionMonitor = new SessionMonitor();
        var codexMonitor = new CodexUsageMonitor();
        var openCodeMonitor = new OpenCodeUsageMonitor();
        var antigravityMonitor = new AntigravityUsageMonitor();
        var notifier = new NotificationService(() => null);
        var updater = new UpdateService();
        var history = new HistoryService(_dir);
        var usageSync = new UsageSyncService(Path.Combine(_dir, "appdata"));
        var weather = new WeatherService();
        var weatherAlert = new WeatherAlertService(
            weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>());

        return new MainViewModel(apiService, credService, sessionMonitor, codexMonitor,
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

    /// <summary>
    /// 재로그인이 필요한 에러에서만 "터미널에서 로그인" 버튼이 보이고, CLI 가 없으면 "설치 후 로그인" 으로 바뀌며,
    /// 에러가 풀리면 사라지는지(#180). 버튼을 누르면 실제 터미널이 뜨므로 명령은 실행하지 않고 바인딩만 본다.
    /// </summary>
    [Fact]
    public async Task UsagePopup_ClaudeLoginAction_FollowsLoginState()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            UsagePopup? popup = null;
            try
            {
                // 띄우지 않은 창은 바인딩 연결이 미뤄지므로 화면 밖에 띄운다.
                popup = new UsagePopup(vm) { Left = -10000 };
                popup.Show();
                popup.UpdateLayout();
                var panel = Assert.IsType<System.Windows.Controls.StackPanel>(popup.FindName("ClaudeLoginAction"));
                var button = Assert.IsType<System.Windows.Controls.Button>(panel.Children[0]);

                vm.ClaudeVm.HasError = true;
                vm.ClaudeVm.NeedsLogin = false;
                Assert.Equal(System.Windows.Visibility.Collapsed, panel.Visibility);

                vm.ClaudeVm.NeedsLogin = true;
                vm.ClaudeVm.IsCliInstalled = false;
                Assert.Equal(System.Windows.Visibility.Visible, panel.Visibility);
                Assert.Same(vm.LaunchClaudeLoginCommand, button.Command);
                Assert.Equal(Loc.ClaudeInstallAndLogin, button.Content);

                vm.ClaudeVm.IsCliInstalled = true;
                Assert.Equal(Loc.ClaudeLoginInTerminal, button.Content);

                vm.ClaudeVm.HasError = false;
                Assert.Equal(System.Windows.Visibility.Collapsed, panel.Visibility);
            }
            finally
            {
                popup?.Close();
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// Antigravity 창 목록이 다른 provider 와 같은 게이지 구조로 그려지는지 — 실제 화면 트리로 확인한다.
    /// 행이 DataTemplate 안에 있어 이름으로 찾을 수 없으므로, 생성된 컨테이너를 훑어
    /// (1) provider 색 게이지 스타일, (2) 시간선 마커, (3) 창 길이를 모르는 행의 마커 숨김을 본다.
    /// </summary>
    [Fact]
    public async Task UsagePopup_AntigravityQuotaUsesTheSameFlatGaugeStructureAsOtherProviders()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            UsagePopup? popup = null;
            try
            {
                var now = DateTimeOffset.Now;
                vm.AntigravityVm.ApplyQuota(
                [
                    new AntigravityModelQuota
                    {
                        ModelId = "gemini-weekly", TokenType = "weekly",
                        RemainingFraction = 0.4, ResetTime = now.AddDays(3.5),
                    },
                    // 창 길이를 모르는 행 — 마커만 빠지고 게이지는 같아야 한다.
                    new AntigravityModelQuota
                    {
                        ModelId = "future-group-monthly", TokenType = "monthly",
                        RemainingFraction = 0.1, ResetTime = now.AddDays(10),
                        DisplayName = "Future Group · Monthly Limit",
                    },
                ], "Antigravity", "Google AI Pro");

                vm.IsAntigravityEnabled = true;
                vm.AntigravityHasData = true;
                vm.AntigravityPaidTierName = vm.AntigravityVm.PaidTierName;
                vm.AntigravityModels = vm.AntigravityVm.Models;
                vm.FocusedProvider = UsageProviderKind.Antigravity;

                popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                var badge = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("AntigravityPlanBadge"));
                Assert.Equal("Google AI Pro", badge.Text);

                var list = Assert.IsType<System.Windows.Controls.ItemsControl>(popup.FindName("AntigravityGaugeList"));
                list.UpdateLayout();
                Assert.Equal(2, list.Items.Count);

                // 두 행 모두 provider 색 게이지를 쓴다 (다른 provider 와 같은 스타일 리소스).
                var bars = FindChildren<System.Windows.Controls.ProgressBar>(list).ToList();
                Assert.Equal(2, bars.Count);
                Assert.All(bars, bar => Assert.Same(popup.FindResource("AntigravityBarStyle"), bar.Style));

                // 시간선 마커는 창 길이를 아는 행(주간)에만 보인다.
                var markers = FindChildren<System.Windows.Controls.Border>(list)
                    .Where(border => Math.Abs(border.Width - 1.5) < 0.01)
                    .ToList();
                Assert.Equal(2, markers.Count);
                Assert.Equal(1, markers.Count(m => m.Visibility == System.Windows.Visibility.Visible));
                Assert.Equal(1, markers.Count(m => m.Visibility == System.Windows.Visibility.Collapsed));

                // 게이지 아래 요약 한 줄 — Claude·Codex 게이지와 같은 자리·같은 문구.
                var texts = FindChildren<System.Windows.Controls.TextBlock>(list)
                    .Select(t => t.Text)
                    .ToList();
                Assert.Contains(Loc.UsageSummary(0.6), texts);
                Assert.Contains(Loc.UsageSummary(0.9), texts);

                // 하단 안내·출처도 다른 provider 상세와 같은 자리에 있다.
                var note = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("AntigravityNoteText"));
                Assert.Equal(Loc.ProviderAntigravityNote, note.Text);
            }
            finally
            {
                popup?.Close();
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// Google AI 크레딧 잔액이 Antigravity 게이지 아래 한 줄로 그려지고, 크레딧 대상이 아니면 줄이 접히는지 — 실제 화면 트리로 확인한다.
    /// </summary>
    [Theory]
    [InlineData(1234L)]
    [InlineData(null)]
    public async Task UsagePopup_AntigravityShowsAiCreditsBelowGauges_OnlyWhenEligible(long? credits)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            UsagePopup? popup = null;
            try
            {
                vm.AntigravityVm.ApplyQuota(
                [
                    new AntigravityModelQuota
                    {
                        ModelId = "gemini-weekly", TokenType = "weekly",
                        RemainingFraction = 0.4, ResetTime = DateTimeOffset.Now.AddDays(3.5),
                    },
                ], "Antigravity", "Google AI Pro", credits);

                vm.IsAntigravityEnabled = true;
                vm.AntigravityHasData = true;
                vm.AntigravityModels = vm.AntigravityVm.Models;
                vm.AntigravityAiCreditsText = vm.AntigravityVm.AiCreditsText;
                vm.FocusedProvider = UsageProviderKind.Antigravity;

                popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                var row = Assert.IsType<System.Windows.Controls.Grid>(popup.FindName("AntigravityAiCreditsRow"));
                if (credits is null)
                {
                    Assert.Equal(System.Windows.Visibility.Collapsed, row.Visibility);
                    return;
                }

                Assert.Equal(System.Windows.Visibility.Visible, row.Visibility);
                var texts = FindChildren<System.Windows.Controls.TextBlock>(row).Select(t => t.Text).ToList();
                Assert.Contains(Loc.AntigravityAiCredits, texts);
                Assert.Contains(1234L.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), texts);
                Assert.Equal(Loc.AntigravityAiCreditsTip, row.ToolTip);
            }
            finally
            {
                popup?.Close();
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// Codex 추가 한도(이미지 생성 등, #182)가 기본 창과 같은 게이지 구조(Codex 색 · 시간선 · 요약 한 줄)로
    /// 기본 창 아래에 그려지는지 — 실제 화면 트리로 확인한다.
    /// </summary>
    [Fact]
    public async Task UsagePopup_CodexAdditionalLimitsUseTheSameGaugeStructureAsTheMainWindows()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            UsagePopup? popup = null;
            try
            {
                var now = DateTimeOffset.Now;
                vm.CodexAdditionalLimits = CodexViewModel.BuildAdditionalLimitRows(
                [
                    new AdditionalQuotaWindow { LimitId = "codex_image_gen", LimitName = "image_gen", UsagePercent = 0.25, ResetAt = now.AddHours(2), WindowMinutes = 300 },
                    new AdditionalQuotaWindow { LimitId = "codex_image_gen", LimitName = "image_gen", UsagePercent = 0.7, ResetAt = now.AddDays(3), WindowMinutes = 10080 },
                ], now, showAbsoluteResetTime: false);
                vm.IsCodexActive = true;
                vm.FocusedProvider = UsageProviderKind.Codex;

                popup = new UsagePopup(vm) { Left = -10000 };
                popup.Show();
                popup.UpdateLayout();

                var list = Assert.IsType<System.Windows.Controls.ItemsControl>(popup.FindName("CodexAdditionalLimitList"));
                list.UpdateLayout();
                Assert.Equal(2, list.Items.Count);

                var bars = FindChildren<System.Windows.Controls.ProgressBar>(list).ToList();
                Assert.Equal(2, bars.Count);
                Assert.All(bars, bar => Assert.Same(popup.FindResource("CodexBarStyle"), bar.Style));

                var markers = FindChildren<System.Windows.Controls.Border>(list)
                    .Where(border => Math.Abs(border.Width - 1.5) < 0.01)
                    .ToList();
                Assert.Equal(2, markers.Count);
                Assert.All(markers, marker => Assert.Equal(System.Windows.Visibility.Visible, marker.Visibility));

                var texts = FindChildren<System.Windows.Controls.TextBlock>(list).Select(t => t.Text).ToList();
                Assert.Contains(Loc.AdditionalQuotaLabel(Loc.CodexImageGenerationLimit, 300), texts);
                Assert.Contains(Loc.AdditionalQuotaLabel(Loc.CodexImageGenerationLimit, 10080), texts);
                Assert.Contains(Loc.UsageSummary(0.7), texts);
            }
            finally
            {
                popup?.Close();
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// Claude 모델 한정 한도(#187)가 7일 게이지 아래에 Claude 색 게이지 · 시간선 · 요약 한 줄로 그려지는지 — 실제 화면 트리로 확인한다.
    /// 리셋 창이 없는(아직 쓰지 않은) 한도는 마커를 숨긴다.
    /// </summary>
    [Fact]
    public async Task UsagePopup_ClaudeModelLimitsUseTheClaudeGaugeStructure()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            UsagePopup? popup = null;
            try
            {
                var now = DateTimeOffset.Now;
                vm.ClaudeVm.SetModelLimits(
                [
                    new AdditionalQuotaWindow { LimitId = "weekly:fable", LimitName = "Fable", UsagePercent = 0.05, ResetAt = now.AddDays(3), WindowMinutes = 10080 },
                    new AdditionalQuotaWindow { LimitId = "seven_day_sonnet", LimitName = "Sonnet", UsagePercent = 0, ResetAt = null, WindowMinutes = 10080 },
                ], now, showAbsoluteResetTime: false);
                vm.ClaudeVm.IsActive = true;
                vm.FocusedProvider = UsageProviderKind.Claude;

                popup = new UsagePopup(vm) { Left = -10000 };
                popup.Show();
                popup.UpdateLayout();

                var list = Assert.IsType<System.Windows.Controls.ItemsControl>(popup.FindName("ClaudeModelLimitList"));
                list.UpdateLayout();
                Assert.Equal(2, list.Items.Count);

                var bars = FindChildren<System.Windows.Controls.ProgressBar>(list).ToList();
                Assert.Equal(2, bars.Count);
                Assert.All(bars, bar => Assert.Same(popup.FindResource("UsageBarStyle"), bar.Style));

                var markers = FindChildren<System.Windows.Controls.Border>(list)
                    .Where(border => Math.Abs(border.Width - 1.5) < 0.01)
                    .ToList();
                Assert.Equal(2, markers.Count);
                Assert.Equal(System.Windows.Visibility.Visible, markers[0].Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, markers[1].Visibility);

                var texts = FindChildren<System.Windows.Controls.TextBlock>(list).Select(t => t.Text).ToList();
                Assert.Contains(Loc.AdditionalQuotaLabel("Fable", 10080), texts);
                Assert.Contains(Loc.AdditionalQuotaLabel("Sonnet", 10080), texts);
                Assert.Contains(Loc.UsageSummary(0.05), texts);
            }
            finally
            {
                popup?.Close();
                vm.Dispose();
            }
        });
    }

    /// <summary>
    /// 다른 PC 가 본 Claude 할당량을 쓸 때 모델 한정 한도(#187)도 같은 스냅샷에서 함께 옮긴다.
    /// 이 필드가 없던 구버전이 쓴 스냅샷이면 비운다 — 남은 행이 다른 시점의 값이 되지 않게.
    /// </summary>
    [Fact]
    public async Task ApplySyncedClaudeQuota_ShowsTheModelLimitsFromThatSnapshot()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                vm.ApplySyncedClaudeQuota(SyncedClaude("laptop", new UsageSyncAdditionalQuota
                {
                    LimitId = "weekly:fable", LimitName = "Fable", UsagePercent = 0.4,
                    ResetAt = DateTimeOffset.Now.AddDays(2), WindowMinutes = 10080,
                }));

                var row = Assert.Single(vm.ClaudeVm.ModelLimits);
                Assert.Equal(Loc.AdditionalQuotaLabel("Fable", 10080), row.DisplayName);
                Assert.Equal(0.4, row.UsagePercent, 6);

                vm.ApplySyncedClaudeQuota(SyncedClaude("old-pc"));

                Assert.Empty(vm.ClaudeVm.ModelLimits);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    private static UsageSyncSnapshot SyncedClaude(string deviceName, params UsageSyncAdditionalQuota[] modelLimits) => new()
    {
        Provider = UsageProviderKind.Claude,
        DeviceId = deviceName,
        DeviceName = deviceName,
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Quota = new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.2,
            ShortResetAt = DateTimeOffset.Now.AddHours(2),
            LongUsagePercent = 0.3,
            LongResetAt = DateTimeOffset.Now.AddDays(2),
            AdditionalQuotas = modelLimits,
        },
    };

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

    // #183: 설정의 Gemini CLI 항목이 Antigravity 로 바뀌었는지 — 표시 체크박스와 트레이 표시 기준 항목이
    // 같은 값을 따라 움직여야 한다(표시를 끈 공급자는 트레이 기준으로 고를 수 없다).
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingsWindow_OffersAntigravityInsteadOfGeminiCli(bool antigravityEnabled)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? window = null;
            try
            {
                vm.IsAntigravityEnabled = antigravityEnabled;
                window = new SettingsWindow(vm);

                var check = Assert.IsType<System.Windows.Controls.CheckBox>(window.FindName("ChkVisibleAntigravity"));
                Assert.Equal(antigravityEnabled, check.IsChecked);

                var combo = Assert.IsType<System.Windows.Controls.ComboBox>(window.FindName("CmbTrayDisplayMode"));
                var tags = combo.Items.Cast<System.Windows.Controls.ComboBoxItem>().Select(i => i.Tag?.ToString()).ToList();
                Assert.Contains(UsageProviderKind.Antigravity, tags);
                Assert.DoesNotContain(UsageProviderKind.LegacyGeminiCli, tags);

                var trayItem = Assert.IsType<System.Windows.Controls.ComboBoxItem>(window.FindName("TrayItemAntigravity"));
                Assert.Equal(antigravityEnabled, trayItem.IsEnabled);
            }
            finally
            {
                window?.Close();
                vm.Dispose();
            }
        });
    }

    // Antigravity 의 Claude·GPT 한도 표시 토글 — 설정창 체크박스가 현재 값을 보여주고,
    // 값을 바꾸면 다시 조회하지 않아도 팝업 행과 트레이 대표값이 함께 바뀐다.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AntigravityThirdPartyToggle_DrivesSettingsCheckboxAndPopupRows(bool show)
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? window = null;
            try
            {
                vm.ShowAntigravityThirdPartyQuota = !show;
                var reset = DateTimeOffset.Now.AddHours(3);
                vm.AntigravityVm.ApplyQuota(
                [
                    new AntigravityModelQuota { ModelId = "gemini-5h", TokenType = "5h", RemainingFraction = 0.9, ResetTime = reset },
                    new AntigravityModelQuota { ModelId = "3p-5h",     TokenType = "5h", RemainingFraction = 0.2, ResetTime = reset },
                ], null, null);
                vm.AntigravityHasData = true;

                vm.ShowAntigravityThirdPartyQuota = show;

                Assert.Equal(show ? new[] { "gemini-5h", "3p-5h" } : new[] { "gemini-5h" }, vm.AntigravityModels.Select(m => m.Key));
                Assert.Equal(show ? 0.8 : 0.1, vm.AntigravityPercent, 3);

                window = new SettingsWindow(vm);
                var check = Assert.IsType<System.Windows.Controls.CheckBox>(window.FindName("ChkShowAntigravityThirdPartyQuota"));
                Assert.Equal(show, check.IsChecked);
                Assert.Equal(Loc.AntigravityShowThirdPartyQuota, check.Content);
            }
            finally
            {
                window?.Close();
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
                // 저장이 실제 사용자 설정이 아니라 테스트 폴더로 갔는지(#186).
                Assert.True(File.Exists(Path.Combine(_dir, "claude-usage-tray.json")));
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

    private static IEnumerable<T> FindChildren<T>(System.Windows.DependencyObject root)
        where T : System.Windows.DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindChildren<T>(child)) yield return nested;
        }
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
