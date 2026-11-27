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

    private MainViewModel CreateViewModel(UsageSyncService? usageSyncOverride = null)
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
        var usageSync = usageSyncOverride ?? new UsageSyncService(Path.Combine(_dir, "appdata"));
        var weather = new WeatherService();
        var weatherAlert = new WeatherAlertService(
            weather, notifier, () => new NotificationSettings(), Array.Empty<IWeatherWarningProvider>());

        // 개발자 PC 의 ~/.claude.json(실명·조직) 대신 테스트 폴더의 프로필을 읽는다(#218).
        var claudeAccount = new ClaudeAccountProfileReader(Path.Combine(_dir, ".claude.json"));

        return new MainViewModel(apiService, credService, sessionMonitor, codexMonitor,
            openCodeMonitor, antigravityMonitor, notifier, settingsService, updater, history, usageSync, weather, weatherAlert,
            claudeAccount: claudeAccount);
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

    // #157: 다른 PC 가 동기화 폴더에 남긴 OpenCode 공식 할당량(롤링·주간·월간)이 새로고침을 거쳐 이 PC 팝업 값이 되는지
    // 끝까지 본다. 쓰기·읽기·변환은 각각 UsageSyncServiceTests·OpenCodeConsoleExtrasTests 가 따로 본다.
    [Fact]
    public async Task RefreshAsync_AppliesOfficialOpenCodeQuotaFromAnotherDevice()
    {
        var syncRoot = Path.Combine(_dir, "sync");
        var now = DateTimeOffset.Now;
        var source = new UsageSyncService(Path.Combine(_dir, "source"), () => now, "source-pc");
        source.WriteSnapshot(syncRoot, source.CreateSnapshot(
            UsageProviderKind.OpenCode,
            null,
            new UsageSyncQuotaSnapshot
            {
                HasData = true,
                ObservedAtUtc = now,
                OpenCode = new UsageSyncOpenCodeQuota
                {
                    Rolling = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.21, ResetAt = now.AddHours(3) },
                    Weekly = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.32, ResetAt = now.AddDays(4) },
                    Monthly = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.43, ResetAt = now.AddDays(20) },
                },
            },
            new UsageSyncLocalTotals { RequestCount = 10 }));

        await WpfTestHost.RunAsync(async () =>
        {
            var receiver = new UsageSyncService(Path.Combine(_dir, "receiver"), () => now, "receiver-pc");
            var vm = CreateViewModel(receiver);
            try
            {
                vm.UsageSyncEnabled = true;
                vm.UsageSyncFolderPath = syncRoot;

                await vm.RefreshAsync();

                Assert.True(vm.OpenCodeVm.HasWebQuota);
                Assert.Equal(0.21, vm.OpenCodeVm.RollingPercent, 6);
                Assert.Equal(0.32, vm.OpenCodeVm.WeeklyPercent, 6);
                Assert.Equal(0.43, vm.OpenCodeVm.MonthlyPercent, 6);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    // #141: 다른 PC 값을 보고 있을 때의 Claude·Codex 출처 문구("○○의 … 스냅샷으로 표시 중")가 언어를 바꾸면 바로 바뀐다.
    // 예전에는 완성된 문장을 담아 두어 다음 조회(최대 2분)까지 옛 언어로 남았다. Codex 는 같은 방식이지만 이 PC 의 실제
    // ~/.codex 로그가 있으면 동기화 값을 쓰지 않아(로컬 우선) 개발 PC 마다 결과가 달라지므로 Claude 로만 끝까지 본다.
    [Fact]
    public async Task SyncOriginLabels_FollowLanguageChange()
    {
        var syncRoot = Path.Combine(_dir, "sync");
        var now = DateTimeOffset.Now;
        var source = new UsageSyncService(Path.Combine(_dir, "source"), () => now, "source-pc");
        source.WriteSnapshot(syncRoot, source.CreateSnapshot(
            UsageProviderKind.Claude,
            null,
            new UsageSyncQuotaSnapshot
            {
                HasData = true,
                ObservedAtUtc = now,
                ShortUsagePercent = 0.4,
                ShortResetAt = now.AddHours(2),
                LongUsagePercent = 0.2,
                LongResetAt = now.AddDays(3),
            },
            new UsageSyncLocalTotals()));

        var previous = Loc.CurrentLang;
        await WpfTestHost.RunAsync(async () =>
        {
            var receiver = new UsageSyncService(Path.Combine(_dir, "receiver"), () => now, "receiver-pc");
            var vm = CreateViewModel(receiver);
            try
            {
                Loc.SetLanguage("ko");
                vm.UsageSyncEnabled = true;
                vm.UsageSyncFolderPath = syncRoot;

                await vm.RefreshAsync();

                Assert.Equal(0.4, vm.ClaudeVm.ShortPercent, 6);
                Assert.StartsWith("source-pc의 ", vm.ClaudeVm.ApiNote);

                Loc.SetLanguage("en");

                Assert.StartsWith("Showing source-pc snapshot", vm.ClaudeVm.ApiNote);
            }
            finally
            {
                Loc.SetLanguage(previous);
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
                // #354 구분선 규칙: 게이지 영역과 토큰 타일 사이에는 선 없이 여백만 둔다(선이 빈 게이지 트랙처럼 읽혔다).
                Assert.Null(popup.FindName("OpenCodeStatsSeparator"));
                Assert.NotNull(popup.FindName("OpenCodeTokenTiles"));
                Assert.NotNull(popup.FindName("OpenCodeLimitReachedNotice"));
                Assert.NotNull(popup.FindName("OpenCodeSubscriptionStatus"));
                Assert.NotNull(popup.FindName("OpenCodeBillingLine"));
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    // #310: OpenCode·Codex 하단 안내는 조회 때만 만들어져, 언어를 바꿔도 다음 새로고침까지 옛 언어로 남았다.
    [Fact]
    public async Task LanguageChange_RebuildsProviderNotesWithoutARefresh()
    {
        var originalLang = Loc.CurrentLang;
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                Loc.SetLanguage("ko");
                var koOpenCode = Loc.ProviderOpenCodeNote;
                var koCodex = Loc.ProviderCodexNote;
                Assert.Equal(koOpenCode, vm.OpenCodeNote);
                Assert.Equal(koCodex, vm.CodexNote);

                Loc.SetLanguage("en");

                Assert.Equal(Loc.ProviderOpenCodeNote, vm.OpenCodeNote);
                Assert.Equal(Loc.ProviderCodexNote, vm.CodexNote);
                Assert.NotEqual(koOpenCode, vm.OpenCodeNote);
                Assert.NotEqual(koCodex, vm.CodexNote);
            }
            finally
            {
                Loc.SetLanguage(originalLang);
                vm.Dispose();
            }
        });
    }

    // 콘솔 응답의 부가 정보(한도 도달·구독 상태·추가 사용/크레딧)가 팝업 줄에 실제로 바인딩되고,
    // 값이 없는 줄은 접힌다.
    [Fact]
    public async Task UsagePopup_ShowsOpenCodeConsoleExtrasOnlyWhenPresent()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                var now = DateTimeOffset.Now;
                vm.OpenCodeVm.ApplySyncedWebUsage(new OpenCodeWebUsage
                {
                    ObservedAtUtc = now.ToUniversalTime(),
                    Rolling = new OpenCodeQuotaWindow { UsagePercent = 1, ResetAt = now.AddHours(2) },
                    Weekly = new OpenCodeQuotaWindow { UsagePercent = 0.4, ResetAt = now.AddDays(3) },
                    Monthly = new OpenCodeQuotaWindow { UsagePercent = 0.2, ResetAt = now.AddDays(20) },
                    CreditsUsd = 4.5m,
                });

                using var popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                var limit = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("OpenCodeLimitReachedNotice"));
                var status = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("OpenCodeSubscriptionStatus"));
                var billing = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("OpenCodeBillingLine"));

                Assert.Equal(vm.OpenCodeVm.LimitReachedLabel, limit.Text);
                Assert.Equal(System.Windows.Visibility.Visible, limit.Visibility);
                Assert.Equal(System.Windows.Visibility.Collapsed, status.Visibility);
                Assert.Equal(vm.OpenCodeVm.BillingLabel, billing.Text);
                Assert.Contains("$4.50", billing.Text);
                Assert.Equal(System.Windows.Visibility.Visible, billing.Visibility);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    // #198: Codex 로그인이 서버에서 거부돼 로컬 로그로 폴백했을 때만 안내 줄이 보이고, 정상으로 돌아오면 접힌다.
    [Fact]
    public async Task UsagePopup_CodexSignInNotice_FollowsSignInState()
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
                var notice = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("CodexSignInNotice"));

                vm.CodexNeedsSignIn = true;
                Assert.Equal(System.Windows.Visibility.Visible, notice.Visibility);
                Assert.Equal(Loc.CodexSignInExpiredNotice, notice.Text);

                vm.CodexNeedsSignIn = false;
                Assert.Equal(System.Windows.Visibility.Collapsed, notice.Visibility);
            }
            finally
            {
                popup?.Close();
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
                var markers = FindChildren<ClaudeUsageTray.Views.TimelineMarker>(list).ToList();
                Assert.Equal(2, markers.Count);
                Assert.Equal(1, markers.Count(m => m.Visibility == System.Windows.Visibility.Visible));
                Assert.Equal(1, markers.Count(m => m.Visibility == System.Windows.Visibility.Collapsed));

                // 요약("60% 사용 · 잔량 40%")은 Claude·Codex 주 게이지(#224)와 같이 막대 아래 중복 줄을 두지 않고
                // 라벨 툴팁에 두며, 라벨 글자 크기도 Claude 주 게이지(11.5pt)와 맞춘다(#346).
                var texts = FindChildren<System.Windows.Controls.TextBlock>(list)
                    .Select(t => t.Text)
                    .ToList();
                Assert.DoesNotContain(Loc.UsageSummary(0.6), texts);
                Assert.DoesNotContain(Loc.UsageSummary(0.9), texts);

                var claudeShortLabel = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("ClaudeShortLabel"));
                var gaugeLabels = FindChildren<System.Windows.Controls.TextBlock>(list)
                    .Where(t => t.Name == "AntigravityGaugeLabel")
                    .ToList();
                Assert.Equal(2, gaugeLabels.Count);
                Assert.All(gaugeLabels, label => Assert.Equal(claudeShortLabel.FontSize, label.FontSize));
                var tooltips = gaugeLabels.Select(l => l.ToolTip as string).ToList();
                Assert.Contains(Loc.UsageSummary(0.6), tooltips);
                Assert.Contains(Loc.UsageSummary(0.9), tooltips);

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
    /// 자격 파일의 등급과 ~/.claude.json 의 계정 이름·조직이 Claude 등급 배지 한 줄에 두 부분으로 그려지고,
    /// 설정을 끄면 등급만 남는지(#218) — 실제 화면 트리로 확인한다.
    /// </summary>
    [Fact]
    public async Task UsagePopup_ClaudeBadgeLineShowsAccountAfterPlan()
    {
        // 만료 시각을 먼 미래로 둬 토큰 갱신(네트워크)이 일어나지 않게 한다.
        File.WriteAllText(Path.Combine(_dir, ".credentials.json"), """
            { "claudeAiOauth": { "accessToken": "t", "expiresAt": 4102444800000,
                                 "subscriptionType": "team", "rateLimitTier": "default_claude_max_5x" } }
            """);
        File.WriteAllText(Path.Combine(_dir, ".claude.json"), """
            { "oauthAccount": { "emailAddress": "someone@example.com", "displayName": "김민준",
                                "organizationName": "Acme Corp", "organizationType": "claude_team" } }
            """);

        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            UsagePopup? popup = null;
            try
            {
                popup = new UsagePopup(vm);
                popup.Left = -10000;
                popup.Show();
                popup.UpdateLayout();

                var badge = Assert.IsType<System.Windows.Controls.TextBlock>(popup.FindName("ClaudePlanBadge"));
                var runs = badge.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text).ToList();
                Assert.Equal(["Claude Team 5x", " · 김민준 · Acme Corp"], runs);
                Assert.Equal("Claude Team 5x · 김민준 · Acme Corp", badge.ToolTip);
                Assert.DoesNotContain("@", badge.Text);

                vm.ShowClaudeAccount = false;
                popup.UpdateLayout();
                runs = badge.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text).ToList();
                Assert.Equal(["Claude Team 5x", ""], runs);
                Assert.Null(badge.ToolTip);
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

                var markers = FindChildren<ClaudeUsageTray.Views.TimelineMarker>(list).ToList();
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

                var markers = FindChildren<ClaudeUsageTray.Views.TimelineMarker>(list).ToList();
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

    // #194 리뷰: 응답에 Claude·GPT 창만 남고 숨김 설정이면 보이는 행이 없다 — 빈 섹션·"0%" 대신 섹션을 접고,
    // 설정을 켜면 다시 조회하지 않아도 바로 나타나야 한다.
    [Fact]
    public async Task AntigravityThirdPartyToggle_HidesTheSectionWhenNoRowIsLeft()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            try
            {
                vm.ShowAntigravityThirdPartyQuota = true;
                vm.AntigravityVm.ApplyQuota(
                [
                    new AntigravityModelQuota { ModelId = "3p-5h", TokenType = "5h", RemainingFraction = 0.4, ResetTime = DateTimeOffset.Now.AddHours(3) },
                ], null, null);
                vm.AntigravityHasData = true;

                vm.ShowAntigravityThirdPartyQuota = false;
                Assert.False(vm.AntigravityHasData);
                Assert.Empty(vm.AntigravityModels);

                vm.ShowAntigravityThirdPartyQuota = true;
                Assert.True(vm.AntigravityHasData);
                Assert.Equal(["3p-5h"], vm.AntigravityModels.Select(m => m.Key));
                Assert.Equal(0.6, vm.AntigravityPercent, 3);
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

    // ── Claude 계정 별명 (#307) ──────────────────────────────────────────────

    private const string AccountUuid = "00000000-0000-0000-0000-0000000000a1";
    private const string PersonalOrg = "00000000-0000-0000-0000-0000000000b1";
    private const string TeamOrg = "00000000-0000-0000-0000-0000000000b2";

    /// <summary>
    /// 테스트 폴더에 로그인한 상태를 만든다. 토큰은 비워 둔다 — 새로고침이 돌아도 API·토큰 갱신을 부르지 않는다.
    /// 최근 Claude Code 처럼 자격 파일에는 조직이 없고 프로필에만 있다(#275).
    /// </summary>
    private void SignIn(string orgUuid)
    {
        File.WriteAllText(Path.Combine(_dir, ".credentials.json"), """
            { "claudeAiOauth": { "accessToken": "", "refreshToken": "", "expiresAt": 0, "subscriptionType": "max" } }
            """);
        File.WriteAllText(Path.Combine(_dir, ".claude.json"), $$"""
            {
              "oauthAccount": {
                "accountUuid": "{{AccountUuid}}",
                "organizationUuid": "{{orgUuid}}",
                "displayName": "김민준",
                "organizationName": "Acme Corp",
                "organizationType": "claude_team"
              }
            }
            """);
    }

    [Fact]
    public async Task ClaudeAccountAlias_StaysWithTheAccountItWasGivenTo()
    {
        await WpfTestHost.RunAsync(() =>
        {
            SignIn(TeamOrg);
            var vm = CreateViewModel();
            try
            {
                Assert.True(vm.CanEditClaudeAccountAlias);
                Assert.Equal("", vm.ClaudeAccountAlias);
                Assert.Equal(" · 김민준 · Acme Corp", vm.ClaudeAccountBadgeText);

                Assert.True(vm.SetClaudeAccountAlias("  회사 "));
                Assert.False(vm.SetClaudeAccountAlias("회사")); // 그대로면 저장할 것이 없다
                vm.SaveSettings();

                // 팝업 배지·휴대폰 카드·앱 푸시가 모두 같은 별명을 쓴다.
                var teamKey = $"{TeamOrg}|{AccountUuid}";
                Assert.Equal("회사", vm.ClaudeAccountAlias);
                Assert.Equal(" · 회사", vm.ClaudeAccountBadgeText);
                Assert.Equal("Claude Max · 회사 (김민준 · Acme Corp)", vm.ClaudePlanBadgeToolTip);
                Assert.Equal("회사", vm.ClaudeMobileAccountLabel(teamKey));
                var push = vm.ClaudePushAccount();
                Assert.Equal(teamKey, push.AccountKey);
                Assert.Equal("회사", push.Alias);
            }
            finally
            {
                vm.Dispose();
            }

            // 같은 사람의 다른 조직(개인 계정)에는 붙지 않는다.
            SignIn(PersonalOrg);
            vm = CreateViewModel();
            try
            {
                Assert.Equal("", vm.ClaudeAccountAlias);
                Assert.Equal(" · 김민준 · Acme Corp", vm.ClaudeAccountBadgeText);
                Assert.Equal("", vm.ClaudePushAccount().Alias);
            }
            finally
            {
                vm.Dispose();
            }

            // 다시 그 계정으로 돌아오면 저장해 둔 별명이 돌아온다.
            SignIn(TeamOrg);
            vm = CreateViewModel();
            try
            {
                Assert.Equal("회사", vm.ClaudeAccountAlias);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    [Fact]
    public async Task ClaudeAccountAlias_CannotBeSetWhileSignedOut()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var vm = CreateViewModel();
            SettingsWindow? window = null;
            try
            {
                Assert.False(vm.CanEditClaudeAccountAlias);
                Assert.False(vm.SetClaudeAccountAlias("회사"));

                window = new SettingsWindow(vm);
                window.Left = -10000;
                window.Show();
                window.UpdateLayout();
                Assert.False(window.TxtClaudeAccountAlias.IsEnabled);
                Assert.Equal(Loc.ClaudeAccountAliasSignedOut, window.LblClaudeAccountAliasHint.Text);
            }
            finally
            {
                window?.Close();
                vm.Dispose();
            }
        });
    }

    // 바깥을 눌러 설정 창이 숨으면 칸의 LostFocus 가 오지 않는다 — 숨을 때 입력해 둔 별명을 저장해야 한다.
    [Fact]
    public async Task SettingsWindow_SavesTheClaudeAccountAliasWhenHidden()
    {
        await WpfTestHost.RunAsync(() =>
        {
            SignIn(TeamOrg);
            var vm = CreateViewModel();
            SettingsWindow? window = null;
            try
            {
                window = new SettingsWindow(vm);
                window.Left = -10000;
                window.Show();
                window.UpdateLayout();
                Assert.True(window.TxtClaudeAccountAlias.IsEnabled);
                Assert.Contains("김민준 · Acme Corp", window.LblClaudeAccountAliasHint.Text);

                window.TxtClaudeAccountAlias.Text = "회사";
                window.Hide();

                Assert.Equal("회사", vm.ClaudeAccountAlias);
                var saved = new SettingsService(Path.Combine(_dir, "claude-usage-tray.json")).Load();
                Assert.Equal("회사", saved.ClaudeAccountAliases[$"{TeamOrg}|{AccountUuid}"]);
            }
            finally
            {
                window?.Close();
                vm.Dispose();
            }
        });
    }

    // PC 간 동기화(#313): 같은 계정을 보는 다른 PC 가 더 최근에 바꾼 별명(지운 것 포함)을 이 PC 도 따른다.
    // 공유 폴더 동기화로 "다른 PC" 의 스냅샷을 흉내 낸다 — PC 직접 동기화도 같은 스냅샷을 주고받는다.
    [Fact]
    public async Task ClaudeAccountAlias_FollowsANewerChangeFromAnotherPc()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            SignIn(TeamOrg);
            var folder = Path.Combine(_dir, "sync");
            Directory.CreateDirectory(folder);
            var aliasKey = $"{TeamOrg}|{AccountUuid}";
            var otherPc = new UsageSyncService(Path.Combine(_dir, "other-pc"), deviceName: "PC-A");
            void OtherPcSays(string alias, DateTimeOffset at)
            {
                var snapshot = otherPc.CreateSnapshot(UsageProviderKind.Claude, TeamOrg, null, new UsageSyncLocalTotals());
                snapshot.AccountAlias = new UsageSyncAccountAlias
                {
                    KeyHash = MainViewModel.ClaudeAliasKeyHash(aliasKey),
                    Alias = alias,
                    UpdatedAtUtc = at,
                };
                otherPc.WriteSnapshot(folder, snapshot);
            }
            async Task RefreshAndApply(MainViewModel model)
            {
                await model.RefreshAsync();
                // 다른 PC 의 별명은 UI 스레드에 올려 적용하므로 한 번 비운다.
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => { }, System.Windows.Threading.DispatcherPriority.Background);
            }

            var vm = CreateViewModel();
            try
            {
                vm.UsageSyncEnabled = true;
                vm.UsageSyncFolderPath = folder;

                OtherPcSays("회사", DateTimeOffset.UtcNow);
                await RefreshAndApply(vm);
                Assert.Equal("회사", vm.ClaudeAccountAlias);
                Assert.Equal(" · 회사", vm.ClaudeAccountBadgeText);
                // 따른 별명은 이 PC 설정에도 저장되고(다음 실행에도 남는다), 이 PC 의 스냅샷으로 다시 나간다(가십).
                var saved = new SettingsService(Path.Combine(_dir, "claude-usage-tray.json")).Load();
                Assert.Equal("회사", saved.ClaudeAccountAliases[aliasKey]);
                Assert.True(saved.ClaudeAccountAliasUpdatedAt.ContainsKey(aliasKey));
                await RefreshAndApply(vm);
                Assert.Equal("회사", ReadOwnClaudeSnapshot(folder).AccountAlias?.Alias);

                // 이 PC 에서 나중에 바꾼 별명은 다른 PC 의 더 옛 기록에 밀리지 않는다.
                Assert.True(vm.SetClaudeAccountAlias("개인"));
                OtherPcSays("회사", DateTimeOffset.UtcNow.AddHours(-1));
                await RefreshAndApply(vm);
                Assert.Equal("개인", vm.ClaudeAccountAlias);

                // 다른 PC 가 더 나중에 지우면 이 PC 도 지운다 — 원래 계정 이름으로 돌아간다.
                OtherPcSays("", DateTimeOffset.UtcNow.AddMinutes(1));
                await RefreshAndApply(vm);
                Assert.Equal("", vm.ClaudeAccountAlias);
                Assert.Equal(" · 김민준 · Acme Corp", vm.ClaudeAccountBadgeText);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    // 같은 팀 조직의 다른 사람이 붙인 별명은 따르지 않는다 — 동기화 묶음(조직 해시)은 같아도 별명 키가 다르다.
    [Fact]
    public async Task ClaudeAccountAlias_IgnoresAnotherPersonInTheSameOrganization()
    {
        await WpfTestHost.RunAsync(async () =>
        {
            SignIn(TeamOrg);
            var folder = Path.Combine(_dir, "sync");
            Directory.CreateDirectory(folder);
            var otherPerson = new UsageSyncService(Path.Combine(_dir, "colleague-pc"), deviceName: "COLLEAGUE");
            var snapshot = otherPerson.CreateSnapshot(UsageProviderKind.Claude, TeamOrg, null, new UsageSyncLocalTotals());
            snapshot.AccountAlias = new UsageSyncAccountAlias
            {
                KeyHash = MainViewModel.ClaudeAliasKeyHash($"{TeamOrg}|00000000-0000-0000-0000-0000000000c9"),
                Alias = "동료",
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            };
            otherPerson.WriteSnapshot(folder, snapshot);

            var vm = CreateViewModel();
            try
            {
                vm.UsageSyncEnabled = true;
                vm.UsageSyncFolderPath = folder;
                await vm.RefreshAsync();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => { }, System.Windows.Threading.DispatcherPriority.Background);

                Assert.Equal("", vm.ClaudeAccountAlias);
                // 별명 기록이 없는 PC 는 동기화에 별명을 싣지 않는다 — "없음" 으로 다른 PC 의 별명을 지우지 않게.
                Assert.Null(ReadOwnClaudeSnapshot(folder).AccountAlias);
            }
            finally
            {
                vm.Dispose();
            }
        });
    }

    /// <summary>공유 폴더에서 이 테스트 VM(앱 데이터 폴더 _dir/appdata)이 쓴 오늘 Claude 스냅샷.</summary>
    private UsageSyncSnapshot ReadOwnClaudeSnapshot(string folder)
    {
        var mine = new UsageSyncService(Path.Combine(_dir, "appdata"));
        var read = mine.ReadSnapshots(folder, UsageProviderKind.Claude, TeamOrg, DateOnly.FromDateTime(DateTime.Now));
        return Assert.Single(read.Snapshots, s => s.DeviceId == mine.DeviceId);
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
