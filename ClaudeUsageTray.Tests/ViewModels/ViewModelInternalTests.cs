using System;
using System.IO;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

public class WeatherViewModelTests
{
    [Theory]
    [InlineData("clear", "☀")]
    [InlineData("mainly_clear", "☀")]
    [InlineData("partly_cloudy", "⛅")]
    [InlineData("overcast", "☁")]
    [InlineData("fog", "☁")]
    [InlineData("drizzle", "☂")]
    [InlineData("freezing_drizzle", "☂")]
    [InlineData("rain", "☔")]
    [InlineData("freezing_rain", "☔")]
    [InlineData("rain_showers", "☔")]
    [InlineData("snow", "❄")]
    [InlineData("snow_grains", "❄")]
    [InlineData("snow_showers", "❄")]
    [InlineData("thunderstorm", "⚡")]
    [InlineData("unknown", "•")]
    [InlineData("", "•")]
    public void GetIcon_ReturnsCorrectIcon(string conditionKey, string expected)
    {
        var result = WeatherViewModel.GetIcon(conditionKey);
        Assert.Equal(expected, result);
    }
}

public class AntigravityViewModelTests
{
    [Theory]
    [InlineData("gemini-2.5-flash", "Gemini 2.5 Flash")]
    [InlineData("gemini-3.1-flash-lite", "Gemini 3.1 Flash Lite")]
    [InlineData("claude-sonnet-4.6", "Claude Sonnet 4.6")]
    [InlineData("gpt-oss-120b", "Gpt Oss 120b")]
    [InlineData("gemini-3-flash-preview", "Gemini 3 Flash Preview")]
    [InlineData("", "(unknown)")]
    public void FormatModelName_FormatsCorrectly(string modelId, string expected)
    {
        var result = AntigravityViewModel.FormatModelName(modelId);
        Assert.Equal(expected, result);
    }

    // 리셋 라벨 포맷은 UsageCalculator.FormatResetLabel 로 합쳤다 — 검증은 UsageCalculatorTests 에 있다.

    [Theory]
    [InlineData("gemini-weekly", "weekly", 7 * 24 * 60)]
    [InlineData("3p-5h", "5h", 5 * 60)]
    // 창 종류를 함께 올리지 않던 버전이 동기화한 값 — 버킷 식별자 꼬리로 창을 알아낸다.
    [InlineData("gemini-weekly", "", 7 * 24 * 60)]
    [InlineData("3p-5h", "", 5 * 60)]
    public void ResolveWindowLength_MapsKnownWindows(string bucketId, string window, int expectedMinutes)
    {
        var length = AntigravityViewModel.ResolveWindowLength(new AntigravityModelQuota
        {
            ModelId = bucketId,
            TokenType = window,
        });

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), length);
    }

    [Theory]
    [InlineData("future-group-monthly", "monthly")]
    [InlineData("gemini-2.5-pro", "")]
    [InlineData("", "")]
    public void ResolveWindowLength_ReturnsNull_WhenWindowUnknown(string bucketId, string window)
    {
        // 길이를 모르면 마커 위치를 지어내지 않는다 — null 이면 화면에 시간선을 그리지 않는다.
        Assert.Null(AntigravityViewModel.ResolveWindowLength(new AntigravityModelQuota
        {
            ModelId = bucketId,
            TokenType = window,
        }));
    }
}

public class ClaudeViewModelTests
{
    [Theory]
    [InlineData("429 Too Many Requests", true)]
    [InlineData("rate_limit exceeded", true)]
    [InlineData("HTTP 500: Internal Server Error", false)]
    [InlineData("Something went wrong", false)]
    public void ParseFriendlyError_DetectsRateLimit(string raw, bool isRateLimit)
    {
        // ParseFriendlyError 는 실제로 MainViewModel 에 있다. 죽은 ClaudeViewModel 중복본은 하드닝에서 제거됨.
        var result = MainViewModel.ParseFriendlyError(raw);
        if (isRateLimit)
            Assert.Equal(Loc.RateLimited, result);
        else
            Assert.DoesNotContain("429", result);
    }
}

public class OpenCodeViewModelTests
{
    [Fact]
    public void CalculateTimeProgress_UsesEachOfficialWindowLength()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.FromHours(9));
        var usage = new OpenCodeWebUsage
        {
            Rolling = new OpenCodeQuotaWindow { ResetAt = now.AddHours(2.5) },
            Weekly = new OpenCodeQuotaWindow { ResetAt = now.AddDays(3.5) },
            // 8/10 12:00 ~ 9/10 12:00 의 정확한 중간이 8/26 00:00 이다.
            Monthly = new OpenCodeQuotaWindow
            {
                ResetAt = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.FromHours(9))
            },
        };

        var timeline = OpenCodeViewModel.CalculateTimeProgress(usage, now);

        Assert.NotNull(timeline.Rolling);
        Assert.NotNull(timeline.Weekly);
        Assert.NotNull(timeline.Monthly);
        Assert.Equal(0.5, timeline.Rolling.Value, 6);
        Assert.Equal(0.5, timeline.Weekly.Value, 6);
        Assert.Equal(0.5, timeline.Monthly.Value, 6);
    }

    [Fact]
    public void CalculateTimeProgress_HidesExpiredWindows()
    {
        var now = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.FromHours(9));
        var expired = new OpenCodeQuotaWindow { ResetAt = now.AddSeconds(-1) };
        var usage = new OpenCodeWebUsage
        {
            Rolling = expired,
            Weekly = expired,
            Monthly = expired,
        };

        var timeline = OpenCodeViewModel.CalculateTimeProgress(usage, now);

        Assert.Null(timeline.Rolling);
        Assert.Null(timeline.Weekly);
        Assert.Null(timeline.Monthly);
    }

    [Fact]
    public void ApplySyncedWebUsage_DoesNotReplaceAnAlreadyAppliedQuota()
    {
        var vm = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService());
        var first = CreateUsage(0.25);
        var later = CreateUsage(0.8);

        vm.ApplySyncedWebUsage(first);
        vm.ApplySyncedWebUsage(later);

        Assert.True(vm.HasWebQuota);
        Assert.Equal(0.25, vm.RollingPercent, 6);
        Assert.Same(first, vm.LastSnapshot.OpenCodeDetails?.WebUsage);
    }

    // OpenCode 를 다른 PC 에서만 쓰는 PC — 로컬 근거(오늘 요청·이번 달 기록·오류)가 하나도 없고
    // 동기화로 받은 값만 있는 상태를 실제 뷰모델 속성으로 표시 규칙에 넣어 본다.
    [Fact]
    public void ReceivedQuota_AloneKeepsTheSectionVisible()
    {
        var vm = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService());
        Assert.False(vm.HasWebQuota);
        Assert.False(vm.HasPeriodUsage);
        Assert.False(vm.HasError);

        vm.ApplySyncedWebUsage(CreateUsage(0.42));
        Assert.True(IsSectionActive(vm));

        // 값이 시효를 넘겨 게이지가 빠지고 "갱신 대기 중" 안내만 남아도 섹션은 그대로여야 한다.
        var stale = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService());
        stale.ApplyStaleSyncedQuotaNotice("DESKTOP-V0JCEPJ", DateTimeOffset.Now.AddHours(-1));
        Assert.False(stale.HasWebQuota);
        Assert.True(IsSectionActive(stale));
    }

    private static bool IsSectionActive(OpenCodeViewModel vm) =>
        MainViewModel.IsOpenCodeSectionActive(
            isEnabled: true, hideInactive: true, requestCount: vm.LastRequestCount,
            hasPeriodUsage: vm.HasPeriodUsage, hasWebQuota: vm.HasWebQuota,
            hasStaleSyncedQuota: vm.HasStaleSyncedQuota, hasError: vm.HasError);

    private static OpenCodeWebUsage CreateUsage(double percent)
    {
        var now = DateTimeOffset.Now;
        return new OpenCodeWebUsage
        {
            Rolling = new OpenCodeQuotaWindow { UsagePercent = percent, ResetAt = now.AddHours(3) },
            Weekly = new OpenCodeQuotaWindow { UsagePercent = percent, ResetAt = now.AddDays(4) },
            Monthly = new OpenCodeQuotaWindow { UsagePercent = percent, ResetAt = now.AddDays(20) },
        };
    }
}

/// <summary>
/// OpenCode 를 다른 PC 에서만 쓰는 PC 에서, 동기화 값의 시효가 지났다고 로그인 버튼을 들이밀지 않는지 검증.
/// </summary>
public class OpenCodeWebLoginPromptTests
{
    private static OpenCodeViewModel CreateViewModel() =>
        new(new OpenCodeUsageMonitor(), new HistoryService());

    private static OpenCodeWebUsage CreateWebUsage() => new()
    {
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Rolling = new OpenCodeQuotaWindow { UsagePercent = 0.12, ResetAt = DateTimeOffset.Now.AddHours(3) },
        Weekly = new OpenCodeQuotaWindow { UsagePercent = 0.34, ResetAt = DateTimeOffset.Now.AddDays(5) },
        Monthly = new OpenCodeQuotaWindow { UsagePercent = 0.56, ResetAt = DateTimeOffset.Now.AddDays(21) },
    };

    // 다른 PC 관측값이 시효만 지난 상태 — 로그인이 풀린 게 아니므로 버튼 대신 안내를 보여준다.
    // 단, 게이지 자리에 요청 수를 그리는 NeedsWebLogin 은 그대로 true 여야 값이 사라지지 않는다.
    [Fact]
    public void StaleSyncedQuota_ReplacesLoginButtonWithNotice()
    {
        var vm = CreateViewModel();
        var observedAt = DateTimeOffset.Now.AddMinutes(-70);

        vm.ApplyStaleSyncedQuotaNotice("DESKTOP-OTHER", observedAt);

        Assert.True(vm.HasStaleSyncedQuota);
        Assert.False(vm.ShowWebLoginButton);
        Assert.True(vm.NeedsWebLogin);
        Assert.Contains("DESKTOP-OTHER", vm.SyncedQuotaNoticeLabel, StringComparison.Ordinal);
        Assert.Contains(observedAt.ToLocalTime().ToString("HH:mm"), vm.SyncedQuotaNoticeLabel, StringComparison.Ordinal);
    }

    // 관측 이력이 아예 없으면(=처음 쓰는 계정) 직접 로그인할 경로는 남아 있어야 한다.
    [Fact]
    public void WithoutAnyObservation_LoginButtonStays()
    {
        var vm = CreateViewModel();

        Assert.False(vm.HasStaleSyncedQuota);
        Assert.True(vm.ShowWebLoginButton);

        vm.ApplyStaleSyncedQuotaNotice("DESKTOP-OTHER", DateTimeOffset.Now.AddMinutes(-70));
        vm.ClearStaleSyncedQuotaNotice();

        Assert.True(vm.ShowWebLoginButton);
        Assert.Equal("", vm.SyncedQuotaNoticeLabel);
    }

    // 유효한 동기화 값이 도착하면 게이지를 그리므로 안내도 버튼도 남아 있으면 안 된다.
    [Fact]
    public void FreshSyncedQuota_ClearsNoticeAndLoginButton()
    {
        var vm = CreateViewModel();
        vm.ApplyStaleSyncedQuotaNotice("DESKTOP-OTHER", DateTimeOffset.Now.AddMinutes(-70));

        vm.ApplySyncedWebUsage(CreateWebUsage());

        Assert.True(vm.HasWebQuota);
        Assert.False(vm.HasStaleSyncedQuota);
        Assert.False(vm.ShowWebLoginButton);
        Assert.False(vm.NeedsWebLogin);
        Assert.Equal("", vm.SyncedQuotaNoticeLabel);
    }

    // 재부팅 직후 네트워크가 아직 안 올라와 탐색이 실패한 상황.
    // 세션은 이 PC 에 그대로 남아 있으므로 로그인 버튼이 아니라 재시도 안내를 보여야 한다.
    [Fact]
    public void UnavailableRead_WithSavedSession_ReplacesLoginButtonWithNotice()
    {
        var vm = CreateViewModel();

        vm.ApplyWebSessionState(OpenCodeWebSessionState.Unavailable, hasSavedSession: true);

        Assert.True(vm.IsWebQuotaUnavailable);
        Assert.False(vm.ShowWebLoginButton);
        Assert.True(vm.ShowWebQuotaUnavailableNotice);
        Assert.True(vm.NeedsWebLogin);
        Assert.False(string.IsNullOrWhiteSpace(vm.WebQuotaUnavailableLabel));
    }

    // 서버가 로그인 페이지로 되돌린 경우에만 세션이 실제로 풀린 것 — 이때는 버튼을 남겨야 한다.
    [Fact]
    public void SignedOutRead_KeepsLoginButton()
    {
        var vm = CreateViewModel();
        vm.ApplyWebSessionState(OpenCodeWebSessionState.Unavailable, hasSavedSession: true);

        vm.ApplyWebSessionState(OpenCodeWebSessionState.SignedOut, hasSavedSession: true);

        Assert.False(vm.IsWebQuotaUnavailable);
        Assert.True(vm.ShowWebLoginButton);
        Assert.False(vm.ShowWebQuotaUnavailableNotice);
    }

    // 이 PC 에서 한 번도 로그인한 적이 없으면 확인 실패여도 로그인 경로를 감추면 안 된다.
    [Fact]
    public void UnavailableRead_WithoutSavedSession_KeepsLoginButton()
    {
        var vm = CreateViewModel();

        vm.ApplyWebSessionState(OpenCodeWebSessionState.Unavailable, hasSavedSession: false);

        Assert.False(vm.IsWebQuotaUnavailable);
        Assert.True(vm.ShowWebLoginButton);
    }

    // 다시 읽어내면 안내는 사라지고 게이지가 돌아온다.
    [Fact]
    public void RecoveredRead_ClearsUnavailableNotice()
    {
        var vm = CreateViewModel();
        vm.ApplyWebSessionState(OpenCodeWebSessionState.Unavailable, hasSavedSession: true);

        vm.ApplySyncedWebUsage(CreateWebUsage());

        Assert.False(vm.IsWebQuotaUnavailable);
        Assert.False(vm.ShowWebQuotaUnavailableNotice);
        Assert.False(vm.ShowWebLoginButton);
    }

    // 안내가 둘 다 걸릴 수 있는 상황에서는 더 구체적인 다른 PC 관측 안내만 보인다.
    [Fact]
    public void StaleSyncedNotice_TakesPrecedenceOverUnavailableNotice()
    {
        var vm = CreateViewModel();
        vm.ApplyWebSessionState(OpenCodeWebSessionState.Unavailable, hasSavedSession: true);

        vm.ApplyStaleSyncedQuotaNotice("DESKTOP-OTHER", DateTimeOffset.Now.AddMinutes(-70));

        Assert.True(vm.HasStaleSyncedQuota);
        Assert.False(vm.ShowWebQuotaUnavailableNotice);
        Assert.False(vm.ShowWebLoginButton);
    }
}

/// <summary>
/// 다중 PC 동기화에서 "무엇을 공유해도 되는가" 규칙과, 받은 값을 화면에 옮기는 경로 검증.
/// </summary>
public class UsageSyncQuotaPolicyTests
{
    [Theory]
    [InlineData(UsageProviderKind.OpenCode, 5, 40)]
    [InlineData(UsageProviderKind.OpenCode, 60, 60)]
    [InlineData(UsageProviderKind.Codex, 5, 5)]
    public void OpenCodeQuotaTtl_CoversWebRetryAndCloudDelay(
        string provider, int configuredMinutes, int expectedMinutes)
    {
        Assert.Equal(
            TimeSpan.FromMinutes(expectedMinutes),
            MainViewModel.UsageSyncQuotaTtl(provider, configuredMinutes));
    }

    // 서버가 계정별로 내려주는 할당량만 공유한다. OpenCode 는 로컬 계산 percent 가 아니라
    // 공식 웹 콘솔의 계정 단위 롤링·주간·월간 값만 공유한다.
    [Theory]
    [InlineData(UsageProviderKind.Codex, true)]
    [InlineData(UsageProviderKind.Antigravity, true)]
    [InlineData(UsageProviderKind.GeminiCli, false)]
    [InlineData(UsageProviderKind.OpenCode, true)]
    public void OnlyAccountLevelQuotaIsShared(string provider, bool shared)
    {
        Assert.Equal(shared, MainViewModel.UsageSyncSharesAccountQuota(provider));
    }

    // 이 PC 에 로컬 사용량이 없고 다른 PC 에만 있는 경우(예: OpenCode 를 다른 PC 에서만 씀),
    // MergeLocalTotals 는 "사용량이 있는 기기" 만 세므로 DeviceCount 가 1 이 된다.
    // 예전처럼 DeviceCount > 1 을 요구하면 그 값이 통째로 버려져
    // HideInactiveProviders 와 맞물려 공급자 섹션이 화면에서 사라졌다.
    [Fact]
    public void RemoteOnlyTotals_AreStillDisplayed()
    {
        var remoteOnly = new UsageSyncMergedLocalTotals
        {
            DeviceCount = 1,
            InputTokens = 248_796,
            OutputTokens = 42_958,
            RequestCount = 179,
        };

        Assert.True(MainViewModel.HasMergedDeviceTotals(remoteOnly));
    }

    [Fact]
    public void MergedTotals_WithoutAnyDevice_AreNotDisplayed()
    {
        Assert.False(MainViewModel.HasMergedDeviceTotals(null));
        Assert.False(MainViewModel.HasMergedDeviceTotals(new UsageSyncMergedLocalTotals { DeviceCount = 0 }));
    }

    // 다른 PC 가 공식 할당량만 올린 날(그 PC 도 오늘 토큰은 0) 을 재현한다.
    // 받는 PC 는 게이지 값을 이미 손에 들고 있으므로, 로컬 사용 기록이 없다는 이유로 섹션을 지우면 안 된다.
    [Fact]
    public void SyncedOfficialQuota_KeepsTheOpenCodeSectionVisible()
    {
        Assert.True(MainViewModel.IsOpenCodeSectionActive(
            isEnabled: true, hideInactive: true, requestCount: 0, hasPeriodUsage: false,
            hasWebQuota: true, hasStaleSyncedQuota: false, hasError: false));
    }

    // 받아 둔 값이 시효를 넘겨 "기기명의 15:02 값 · 갱신 대기 중" 안내로 바뀐 상태.
    // 여기서 섹션을 접으면 그 안내를 볼 수 있는 화면 자체가 사라진다.
    [Fact]
    public void AwaitingSyncedQuotaRefresh_KeepsTheOpenCodeSectionVisible()
    {
        Assert.True(MainViewModel.IsOpenCodeSectionActive(
            isEnabled: true, hideInactive: true, requestCount: 0, hasPeriodUsage: false,
            hasWebQuota: false, hasStaleSyncedQuota: true, hasError: false));
    }

    [Theory]
    // 보여 줄 것이 하나도 없으면 종전대로 숨긴다.
    [InlineData(true, true, 0, false, false, false, false, false)]
    // 공급자 표시를 꺼 두었으면 동기화 값이 있어도 숨긴다.
    [InlineData(false, true, 0, false, true, true, true, false)]
    // 자동 숨김이 꺼져 있으면 아무 근거가 없어도 남긴다.
    [InlineData(true, false, 0, false, false, false, false, true)]
    // 합산 요청 수·이번 달 로컬 기록·오류는 종전 그대로 표시 근거다.
    [InlineData(true, true, 179, false, false, false, false, true)]
    [InlineData(true, true, 0, true, false, false, false, true)]
    [InlineData(true, true, 0, false, false, false, true, true)]
    public void OpenCodeSectionVisibility_FollowsTheDisplayRule(
        bool isEnabled, bool hideInactive, int requestCount, bool hasPeriodUsage,
        bool hasWebQuota, bool hasStaleSyncedQuota, bool hasError, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsOpenCodeSectionActive(
            isEnabled, hideInactive, requestCount, hasPeriodUsage,
            hasWebQuota, hasStaleSyncedQuota, hasError));
    }

    // 구독 중인 사용자가 오늘 아직 Claude 를 쓰지 않은 아침. 5시간 창은 정상 조회되어 0% 이고
    // 오늘 토큰도 0 이다 — 여기서 섹션을 접으면 "0% 남았다"가 아니라 "Claude 가 없다"로 보인다.
    [Fact]
    public void PaidSubscriptionBeforeAnyUsage_KeepsTheClaudeSectionVisible()
    {
        Assert.True(MainViewModel.IsClaudeSectionActive(
            isEnabled: true, hideInactive: true, todayTokens: 0, shortPercent: 0,
            hasQuotaData: false, isSubscribed: true, hasError: false));
    }

    // API 가 5시간·7일 창을 0% 로 정상 회신한 상태. 게이지 값을 손에 들고 있으므로
    // 오늘 사용 기록이 없다는 이유로 그 값을 감추면 안 된다.
    [Fact]
    public void FetchedQuotaAtZeroPercent_KeepsTheClaudeSectionVisible()
    {
        Assert.True(MainViewModel.IsClaudeSectionActive(
            isEnabled: true, hideInactive: true, todayTokens: 0, shortPercent: 0,
            hasQuotaData: true, isSubscribed: false, hasError: false));
    }

    // 실제 자격 파일에서 등급을 읽어 표시 규칙까지 이어지는지 확인한다.
    // Max 구독으로 로그인만 해 둔 PC(오늘 사용 0, 할당량 조회 실패) 를 재현한다.
    [Fact]
    public void SubscriptionTypeFromCredentialsFile_ReachesTheDisplayRule()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cut-claude-section-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, ".credentials.json");
        try
        {
            File.WriteAllText(path, """
            {
              "claudeAiOauth": {
                "accessToken": "sk-test-token",
                "expiresAt": 0,
                "subscriptionType": "max",
                "rateLimitTier": "default_claude_max_5x"
              }
            }
            """);

            using var credentials = new CredentialService(path);
            var (subType, _) = credentials.GetSubscriptionInfo();

            Assert.True(MainViewModel.IsPaidClaudeSubscription(subType));
            Assert.True(MainViewModel.IsClaudeSectionActive(
                isEnabled: true, hideInactive: true, todayTokens: 0, shortPercent: 0,
                hasQuotaData: false, isSubscribed: MainViewModel.IsPaidClaudeSubscription(subType),
                hasError: false));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Theory]
    [InlineData("max", true)]
    [InlineData("pro", true)]
    [InlineData("team", true)]
    [InlineData("Free", false)]
    [InlineData("free", false)]
    // 로그아웃·필드 부재를 구독으로 단정하지 않는다.
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void PaidSubscriptionCheck_ExcludesFreeAndUnknown(string? subscriptionType, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsPaidClaudeSubscription(subscriptionType));
    }

    [Theory]
    // 로그인도 조회 결과도 없으면 종전대로 숨긴다.
    [InlineData(true, true, 0L, 0.0, false, false, false, false)]
    // 공급자 표시를 꺼 두었으면 구독 중이어도 숨긴다.
    [InlineData(false, true, 0L, 0.0, true, true, true, false)]
    // 자동 숨김이 꺼져 있으면 아무 근거가 없어도 남긴다.
    [InlineData(true, false, 0L, 0.0, false, false, false, true)]
    // 오늘 토큰·5시간 창 사용률·오류는 종전 그대로 표시 근거다.
    [InlineData(true, true, 248_796L, 0.0, false, false, false, true)]
    [InlineData(true, true, 0L, 0.37, false, false, false, true)]
    [InlineData(true, true, 0L, 0.0, false, false, true, true)]
    public void ClaudeSectionVisibility_FollowsTheDisplayRule(
        bool isEnabled, bool hideInactive, long todayTokens, double shortPercent,
        bool hasQuotaData, bool isSubscribed, bool hasError, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsClaudeSectionActive(
            isEnabled, hideInactive, todayTokens, shortPercent,
            hasQuotaData, isSubscribed, hasError));
    }

    // 실측 재현(2026-08-28, DESKTOP-V0JCEPJ): ChatGPT Plus 구독 중이고 마지막 Codex 세션은 이틀 전.
    // 5시간 창은 리셋이 지나 버려져 0% 이지만 주간 창은 2026-09-01 리셋까지 21% 가 살아 있었다.
    // 종전 판정은 5시간 창만 봐서, 이 21% 와 `ChatGPT Plus` 배지를 손에 들고도 섹션을 통째로 지웠다.
    [Fact]
    public void ExpiredShortWindowButLiveWeeklyQuota_KeepsTheCodexSectionVisible()
    {
        Assert.True(MainViewModel.IsCodexSectionActive(
            isEnabled: true, hideInactive: true, hasTodayUsage: false, shortPercent: 0, longPercent: 0.21,
            hasQuotaData: true, isSubscribed: true, hasError: false));
    }

    // 두 창 모두 갓 초기화되어 0% 인 순간. 조회에는 성공했으므로 "값이 0" 이지 "값이 없다" 가 아니다.
    [Fact]
    public void FreshlyResetCodexWindows_KeepTheSectionVisible()
    {
        Assert.True(MainViewModel.IsCodexSectionActive(
            isEnabled: true, hideInactive: true, hasTodayUsage: false, shortPercent: 0, longPercent: 0,
            hasQuotaData: true, isSubscribed: false, hasError: false));
    }

    // 창 정보가 아예 없어도(로그가 오래됐고 동기화도 비어 있음) 유료 구독 사실만으로 섹션을 남긴다.
    [Fact]
    public void PaidCodexSubscriptionWithoutAnyQuota_KeepsTheSectionVisible()
    {
        Assert.True(MainViewModel.IsCodexSectionActive(
            isEnabled: true, hideInactive: true, hasTodayUsage: false, shortPercent: 0, longPercent: 0,
            hasQuotaData: false, isSubscribed: true, hasError: false));
    }

    // 로그인 파일에서 요금제를 읽어 표시 규칙까지 이어지는지 확인한다 —
    // 오늘 Codex 를 쓰지 않아 세션 로그의 rate_limits 가 없는 PC 를 재현한다.
    [Fact]
    public void PlanTypeFromCodexAuthFile_ReachesTheDisplayRule()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cut-codex-section-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "auth.json");
        try
        {
            File.WriteAllText(path, $$"""
            {
              "auth_mode": "chatgpt",
              "OPENAI_API_KEY": null,
              "tokens": {
                "id_token": "{{JwtWithPlanType("plus")}}",
                "account_id": "00000000-0000-0000-0000-000000000000"
              }
            }
            """);

            var planType = CodexUsageMonitor.TryReadPlanTypeFromAuth(path);

            Assert.Equal("plus", planType);
            Assert.True(MainViewModel.IsPaidCodexSubscription(planType));
            Assert.True(MainViewModel.IsCodexSectionActive(
                isEnabled: true, hideInactive: true, hasTodayUsage: false, shortPercent: 0, longPercent: 0,
                hasQuotaData: false, isSubscribed: MainViewModel.IsPaidCodexSubscription(planType),
                hasError: false));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // 리뷰 지적(#155): 로그아웃해도 세션 로그의 rate_limits.plan_type 은 남는다 —
    // ProcessFile 이 날짜와 무관하게 가장 최근 rate_limits 를 집어오고, DropExpiredWindows 는 창만 버리고
    // plan_type 은 건드리지 않기 때문이다. 그 값으로 구독을 판정하면 해지·로그아웃한 PC 에서 섹션이
    // 영영 접히지 않으므로, 근거는 반드시 "지금 로그인된 계정"(GetCurrentPlanType) 에서 와야 한다.
    [Fact]
    public void StalePlanTypeInOldSessionLog_DoesNotCountAsSubscription()
    {
        var root = Path.Combine(Path.GetTempPath(), "cut-codex-signedout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // 사흘 전 세션 로그 — 창은 이미 만료됐지만 plan_type 은 그대로 남아 있다.
            var expired = DateTimeOffset.Now.AddDays(-3).ToUnixTimeSeconds();
            File.WriteAllText(Path.Combine(root, "rollout-old.jsonl"),
                """{"timestamp":"2026-08-25T01:00:00.000Z","type":"event_msg","payload":{"type":"token_count","info":{"total_token_usage":{"input_tokens":999,"cached_input_tokens":0,"output_tokens":9,"reasoning_output_tokens":0,"total_tokens":1008}},"rate_limits":{"limit_id":"codex","primary":{"used_percent":61.0,"window_minutes":300,"resets_at":"""
                + expired + """},"secondary":null,"plan_type":"plus"}}}""" + Environment.NewLine);

            var snapshot = new CodexUsageMonitor().GetTodaySnapshot(root);

            // 로그에는 남는다(배지 문구는 이 값을 쓴다) — 그래서 판정에 그대로 쓰면 안 된다.
            Assert.Equal("plus", snapshot.PlanType);
            Assert.Equal(0, snapshot.ShortUsagePercent);
            Assert.Null(snapshot.ShortResetAt);

            // 로그아웃 상태 — auth.json 이 없다.
            var currentPlanType = CodexUsageMonitor.TryReadPlanTypeFromAuth(Path.Combine(root, "auth.json"));
            Assert.Null(currentPlanType);

            Assert.False(MainViewModel.IsCodexSectionActive(
                isEnabled: true, hideInactive: true, hasTodayUsage: false,
                shortPercent: snapshot.ShortUsagePercent, longPercent: snapshot.LongUsagePercent,
                hasQuotaData: false,
                isSubscribed: MainViewModel.IsPaidCodexSubscription(currentPlanType),
                hasError: false));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    /// <summary>서명 없는 표시용 id_token — payload 만 읽는 <c>TryReadPlanTypeFromAuth</c> 의 입력을 만든다.</summary>
    private static string JwtWithPlanType(string planType)
    {
        var payload = "{\"https://api.openai.com/auth\":{\"chatgpt_plan_type\":\"" + planType + "\"}}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"header.{encoded}.signature";
    }

    [Theory]
    [InlineData("plus", true)]
    [InlineData("pro", true)]
    [InlineData("team", true)]
    [InlineData("enterprise", true)]
    [InlineData("Free", false)]
    [InlineData("free", false)]
    // API 키 모드·로그아웃처럼 요금제를 모르는 상태를 구독으로 단정하지 않는다.
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void PaidCodexSubscriptionCheck_ExcludesFreeAndUnknown(string? planType, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsPaidCodexSubscription(planType));
    }

    [Theory]
    // 로그인도 조회 결과도 없으면 종전대로 숨긴다.
    [InlineData(true, true, false, 0.0, 0.0, false, false, false, false)]
    // 공급자 표시를 꺼 두었으면 구독 중이어도 숨긴다.
    [InlineData(false, true, true, 0.0, 0.0, true, true, true, false)]
    // 자동 숨김이 꺼져 있으면 아무 근거가 없어도 남긴다.
    [InlineData(true, false, false, 0.0, 0.0, false, false, false, true)]
    // 5시간 창 사용률·오류는 종전 그대로 표시 근거다.
    [InlineData(true, true, false, 0.37, 0.0, false, false, false, true)]
    [InlineData(true, true, false, 0.0, 0.0, false, false, true, true)]
    // 오늘 토큰 기록도 근거다 — 창이 모두 만료돼 버려진 날에도 "오늘 썼다" 는 남는다.
    [InlineData(true, true, true, 0.0, 0.0, false, false, false, true)]
    public void CodexSectionVisibility_FollowsTheDisplayRule(
        bool isEnabled, bool hideInactive, bool hasTodayUsage, double shortPercent, double longPercent,
        bool hasQuotaData, bool isSubscribed, bool hasError, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsCodexSectionActive(
            isEnabled, hideInactive, hasTodayUsage, shortPercent, longPercent,
            hasQuotaData, isSubscribed, hasError));
    }

    [Fact]
    public void DeviceDerivedPercent_IsNeverWrittenToTheSharedFolder()
    {
        var snapshot = new ProviderUsageSnapshot
        {
            ShortUsagePercent = 0.73,
            ShortResetAt = DateTimeOffset.Now.AddHours(2),
        };

        Assert.Null(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.GeminiCli, snapshot));
        Assert.Null(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.OpenCode, snapshot));
        Assert.NotNull(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, snapshot));
    }

    [Fact]
    public void OpenCodeOfficialQuota_RoundTripsAllWindows()
    {
        var observedAt = DateTimeOffset.Now.AddMinutes(-10);
        var rollingReset = DateTimeOffset.Now.AddHours(3);
        var weeklyReset = DateTimeOffset.Now.AddDays(4);
        var monthlyReset = DateTimeOffset.Now.AddDays(20);
        var snapshot = new ProviderUsageSnapshot
        {
            OpenCodeDetails = new OpenCodeUsageDetails
            {
                WebUsage = new OpenCodeWebUsage
                {
                    ObservedAtUtc = observedAt,
                    Rolling = new OpenCodeQuotaWindow { UsagePercent = 0.31, ResetAt = rollingReset },
                    Weekly = new OpenCodeQuotaWindow { UsagePercent = 0.42, ResetAt = weeklyReset },
                    Monthly = new OpenCodeQuotaWindow { UsagePercent = 0.53, ResetAt = monthlyReset },
                },
            },
        };

        var quota = MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.OpenCode, snapshot);
        var restored = MainViewModel.CreateOpenCodeWebUsage(quota);

        Assert.NotNull(quota?.OpenCode);
        Assert.NotNull(restored);
        Assert.Equal(observedAt, restored!.ObservedAtUtc);
        Assert.Equal(0.31, restored.Rolling.UsagePercent, 6);
        Assert.Equal(rollingReset, restored.Rolling.ResetAt);
        Assert.Equal(0.42, restored.Weekly.UsagePercent, 6);
        Assert.Equal(weeklyReset, restored.Weekly.ResetAt);
        Assert.Equal(0.53, restored.Monthly.UsagePercent, 6);
        Assert.Equal(monthlyReset, restored.Monthly.ResetAt);
    }

    [Fact]
    public void OpenCodeSyncedQuota_IsRejectedAfterRollingReset()
    {
        var now = DateTimeOffset.Now;
        var quota = new UsageSyncQuotaSnapshot
        {
            HasData = true,
            OpenCode = new UsageSyncOpenCodeQuota
            {
                Rolling = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddSeconds(-1) },
                Weekly = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddDays(2) },
                Monthly = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddDays(20) },
            },
        };

        Assert.Null(MainViewModel.CreateOpenCodeWebUsage(quota, now));
    }

    // 창 길이가 빠지면 받는 PC 가 5시간으로 가정할 수밖에 없어 주간 창에서 시간선이 어긋난다.
    [Fact]
    public void CodexQuota_CarriesWindowLengths()
    {
        var quota = MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, new ProviderUsageSnapshot
        {
            ShortUsagePercent = 0.61,
            ShortResetAt = DateTimeOffset.Now.AddHours(3),
            ShortWindowMinutes = 10080,
            LongUsagePercent = 0.2,
            LongResetAt = DateTimeOffset.Now.AddDays(3),
            LongWindowMinutes = 43200,
            PlanType = "Plus",
        });

        Assert.NotNull(quota);
        Assert.Equal(10080, quota!.ShortWindowMinutes);
        Assert.Equal(43200, quota.LongWindowMinutes);
        Assert.True(quota.HasLongWindow);
        Assert.Equal("Plus", quota.PlanType);
    }

    // 리셋 시각이 없으면 어느 창의 값인지 알 수 없다 — 사용률만 떠서는 공유해도 쓸 데가 없다.
    [Fact]
    public void QuotaWithoutAnyResetTime_IsNotShared()
    {
        Assert.Null(MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.Codex, new ProviderUsageSnapshot
        {
            ShortUsagePercent = 0.5,
            PlanType = "Plus",
        }));
    }

    // Antigravity 에 로그인하지 않은 PC 도, 받은 모델 목록으로 로컬 조회와 같은 패널을 그린다.
    [Fact]
    public void Antigravity_AppliesSyncedModelRows_LikeALocalLookup()
    {
        var vm = new AntigravityViewModel(new AntigravityUsageMonitor());
        var resetAt = DateTimeOffset.Now.AddHours(4);

        vm.ApplyQuota(
            [
                new AntigravityModelQuota { ModelId = "gemini-3-pro", RemainingFraction = 0.25, ResetTime = resetAt },
                new AntigravityModelQuota { ModelId = "claude-sonnet-4-5", RemainingFraction = 0.9, ResetTime = resetAt },
                // 내부용 모델은 로컬 경로와 똑같이 제외된다.
                new AntigravityModelQuota { ModelId = "chat_default", RemainingFraction = 0.1, ResetTime = resetAt },
                // 리셋 시각이 없는 행도 제외.
                new AntigravityModelQuota { ModelId = "tab_default", RemainingFraction = 0.1, ResetTime = null },
            ],
            "Gemini Code Assist",
            "Gemini Code Assist in Google One AI Pro");

        Assert.True(vm.HasData);
        Assert.Equal("Gemini Code Assist", vm.TierName);
        Assert.Equal(2, vm.Models.Count);
        Assert.Equal("gemini-3-pro", vm.Models[0].ModelId);   // 사용률 내림차순
        Assert.Equal(0.75, vm.Models[0].UsagePercent, 6);
        Assert.Equal(0.10, vm.Models[1].UsagePercent, 6);
        // 한도는 창마다 따로 걸리므로 평균이 아니라 가장 많이 쓴 쪽을 대표값으로 쓴다.
        // 평균이면 여기서 42.5% 가 되어, 이미 75% 를 쓴 제약이 화면에서 가려진다.
        Assert.Equal(0.75, vm.Percent, 6);
    }
}
