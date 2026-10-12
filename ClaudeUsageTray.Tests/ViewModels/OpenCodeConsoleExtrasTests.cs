using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// OpenCode 콘솔 응답에 함께 오는 요금제·한도 도달·구독 상태·추가 사용·크레딧 표시.
/// 문구는 언어(전역 상태)에 따라 바뀌므로 Loc 을 건드리는 다른 테스트와 같은 컬렉션에서 직렬로 돈다.
/// </summary>
[Collection("WpfTests")]
public class OpenCodeConsoleExtrasTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.FromHours(9));

    private static OpenCodeWebUsage Usage(double rolling = 0.2, double weekly = 0.3, double monthly = 0.4) => new()
    {
        ObservedAtUtc = Now.ToUniversalTime(),
        Rolling = new OpenCodeQuotaWindow { UsagePercent = rolling, ResetAt = Now.AddHours(2) },
        Weekly = new OpenCodeQuotaWindow { UsagePercent = weekly, ResetAt = Now.AddDays(3) },
        Monthly = new OpenCodeQuotaWindow { UsagePercent = monthly, ResetAt = Now.AddDays(20) },
    };

    private static T InLanguage<T>(string lang, Func<T> read)
    {
        var original = Loc.CurrentLang;
        Loc.SetLanguage(lang);
        try
        {
            return read();
        }
        finally
        {
            Loc.SetLanguage(original);
        }
    }

    private static T InEnglish<T>(Func<T> read) => InLanguage("en", read);

    [Fact]
    public void LimitReached_IsEmptyWhileEveryWindowHasRoom()
    {
        Assert.Equal("", InEnglish(() => OpenCodeViewModel.FormatLimitReached(Usage(0.99, 0.5, 0.5), Now, false)));
    }

    // 콘솔과 같이 다 쓴 창 가운데 가장 늦게 풀리는 창을 알린다 — 롤링이 먼저 풀려도 주간이 막고 있다.
    [Fact]
    public void LimitReached_NamesTheExhaustedWindowThatResetsLast()
    {
        var label = InEnglish(() => OpenCodeViewModel.FormatLimitReached(Usage(1, 1, 0.5), Now, false));

        Assert.Equal("Weekly limit reached · Go resumes in 3d 0h", label);
    }

    [Fact]
    public void LimitReached_WithExtraUsage_SaysCreditsCarryOn()
    {
        var usage = Usage(rolling: 1) with { ExtraUsageEnabled = true };

        var label = InEnglish(() => OpenCodeViewModel.FormatLimitReached(usage, Now, false));

        Assert.Equal("Rolling limit reached · using credits for the next 2h 0m", label);
    }

    // 해지 예정인데 월간 창을 다 썼으면 다시 열리지 않고 그대로 끝난다.
    [Fact]
    public void LimitReached_OnTheLastMonthOfACancelledPlan_SaysGoEnds()
    {
        var usage = Usage(monthly: 1) with { Subscription = OpenCodeGoSubscriptionStatus.Ends };

        var label = InEnglish(() => OpenCodeViewModel.FormatLimitReached(usage, Now, false));

        Assert.Equal("Monthly limit reached · Go ends in 20d 0h", label);
    }

    // 이미 풀린 창의 100% 는 낡은 값이라 한도 도달로 보지 않는다.
    [Fact]
    public void LimitReached_IgnoresWindowsThatAlreadyReset()
    {
        var usage = Usage(rolling: 1) with
        {
            Rolling = new OpenCodeQuotaWindow { UsagePercent = 1, ResetAt = Now.AddMinutes(-1) },
        };

        Assert.Equal("", InEnglish(() => OpenCodeViewModel.FormatLimitReached(usage, Now, false)));
    }

    [Fact]
    public void LimitReached_AppendsTheClockTimeWhenAbsoluteResetTimesAreOn()
    {
        var usage = Usage(weekly: 1);

        var label = InEnglish(() => OpenCodeViewModel.FormatLimitReached(usage, Now, true));

        Assert.EndsWith($"3d 0h ({UsageCalculator.FormatClockStamp(usage.Weekly.ResetAt, Now)})", label);
    }

    [Fact]
    public void SubscriptionStatus_IsHiddenWhileRenewingNormally()
    {
        Assert.Equal("", InEnglish(() => OpenCodeViewModel.FormatSubscriptionStatus(Usage())));
    }

    [Fact]
    public void SubscriptionStatus_ShowsWhenACancelledPlanEnds()
    {
        var periodEnd = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var usage = Usage() with { Subscription = OpenCodeGoSubscriptionStatus.Ends, PeriodEndsAt = periodEnd };

        var label = InEnglish(() => OpenCodeViewModel.FormatSubscriptionStatus(usage));

        Assert.Equal($"Go subscription ends {periodEnd.ToLocalTime():MM/dd}", label);
    }

    // 기간 끝이 빠진 응답이면 월간 창의 초기화 시각(= 구독 기간 끝)으로 대신한다.
    [Fact]
    public void SubscriptionStatus_FallsBackToTheMonthlyResetForThePeriodEnd()
    {
        var usage = Usage() with { Subscription = OpenCodeGoSubscriptionStatus.SwitchesToGo };

        var label = InEnglish(() => OpenCodeViewModel.FormatSubscriptionStatus(usage));

        Assert.Equal($"Switches from Go Plus to Go on {usage.Monthly.ResetAt.ToLocalTime():MM/dd}", label);
    }

    [Theory]
    [InlineData(true, 1234.5, "Extra usage on · Credits $1,234.50")]
    [InlineData(false, 0d, "Credits $0.00")]
    [InlineData(true, null, "Extra usage on")]
    [InlineData(false, null, "")]
    public void Billing_CombinesExtraUsageAndCredits(bool extraUsage, double? credits, string expected)
    {
        var usage = Usage() with { ExtraUsageEnabled = extraUsage, CreditsUsd = (decimal?)credits };

        Assert.Equal(expected, InEnglish(() => OpenCodeViewModel.FormatBilling(usage)));
    }

    // 팝업이 바인딩하는 값 — 다른 PC 에서 받은 관측값으로도 같은 줄이 그려진다.
    [Fact]
    public void AppliedUsage_FillsBadgeAndExtraLines()
    {
        var vm = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService());
        var now = DateTimeOffset.Now;
        var usage = new OpenCodeWebUsage
        {
            ObservedAtUtc = now.ToUniversalTime(),
            Rolling = new OpenCodeQuotaWindow { UsagePercent = 0.1, ResetAt = now.AddHours(2) },
            Weekly = new OpenCodeQuotaWindow { UsagePercent = 1, ResetAt = now.AddDays(3) },
            Monthly = new OpenCodeQuotaWindow { UsagePercent = 0.5, ResetAt = now.AddDays(20) },
            Product = "go-plus",
            ExtraUsageEnabled = true,
            CreditsUsd = 3m,
            Subscription = OpenCodeGoSubscriptionStatus.PaymentFailed,
        };

        InEnglish(() =>
        {
            vm.ApplySyncedWebUsage(usage);
            return 0;
        });

        Assert.Equal("OpenCode Go Plus", vm.PlanLabel);
        Assert.StartsWith("Weekly limit reached · using credits", vm.LimitReachedLabel);
        Assert.Equal("Go renewal payment failed — check your payment method", vm.SubscriptionStatusLabel);
        Assert.True(vm.IsSubscriptionStatusUrgent);
        Assert.Equal("Extra usage on · Credits $3.00", vm.BillingLabel);
    }

    [Fact]
    public void InactiveSubscription_ShowsItsReasonAndKeepsItAcrossLanguageChanges()
    {
        var vm = new OpenCodeViewModel(new OpenCodeUsageMonitor(), new HistoryService());

        var (english, korean) = InEnglish(() =>
        {
            vm.ApplyWebSessionState(OpenCodeWebSessionState.NoQuota, hasSavedSession: true,
                OpenCodeGoSubscriptionStatus.Paused);
            var en = vm.WebLoginError;
            var ko = InLanguage("ko", () =>
            {
                vm.RefreshLocalizedLabels();
                return vm.WebLoginError;
            });
            return (en, ko);
        });

        Assert.Equal("OpenCode Go is paused — it resumes once the renewal payment succeeds", english);
        Assert.Equal("OpenCode Go 일시정지 — 갱신 결제가 성공하면 다시 쓸 수 있습니다", korean);
        Assert.True(vm.ShowWebLoginButton);
    }

    [Fact]
    public void NoSubscription_KeepsTheSignInHint()
    {
        Assert.Equal(
            InEnglish(() => Loc.OpenCodeWebNoGoQuota),
            InEnglish(() => OpenCodeViewModel.FormatNoGoQuotaNotice(null)));
    }

    // 다른 PC 가 올린 관측에도 요금제·구독·크레딧이 따라와야 같은 줄이 그려진다.
    [Fact]
    public void SyncedQuota_RoundTripsPlanSubscriptionAndCredits()
    {
        var now = DateTimeOffset.Now;
        var periodEnd = now.AddDays(20);
        var snapshot = new ProviderUsageSnapshot
        {
            OpenCodeDetails = new OpenCodeUsageDetails
            {
                WebUsage = Usage() with
                {
                    ObservedAtUtc = now.ToUniversalTime(),
                    Rolling = new OpenCodeQuotaWindow { ResetAt = now.AddHours(2) },
                    Weekly = new OpenCodeQuotaWindow { ResetAt = now.AddDays(3) },
                    Monthly = new OpenCodeQuotaWindow { ResetAt = periodEnd },
                    Product = "go-plus",
                    ExtraUsageEnabled = true,
                    CreditsUsd = 12.34m,
                    Subscription = OpenCodeGoSubscriptionStatus.Ends,
                    PeriodEndsAt = periodEnd,
                },
            },
        };

        var quota = MainViewModel.CreateProviderQuotaSnapshot(UsageProviderKind.OpenCode, snapshot);
        var restored = MainViewModel.CreateOpenCodeWebUsage(quota, now);

        Assert.NotNull(restored);
        Assert.Equal("go-plus", restored.Product);
        Assert.True(restored.ExtraUsageEnabled);
        Assert.Equal(12.34m, restored.CreditsUsd);
        Assert.Equal(OpenCodeGoSubscriptionStatus.Ends, restored.Subscription);
        Assert.Equal(periodEnd, restored.PeriodEndsAt);
    }

    // 이후 버전이 늘린 상태 이름이나 숫자 값은 정상 갱신으로 보고 상태 줄을 감춘다.
    [Theory]
    [InlineData("Suspended")]
    [InlineData("3")]
    [InlineData(null)]
    public void SyncedQuota_TreatsUnknownSubscriptionNamesAsRenewing(string? name)
    {
        var now = DateTimeOffset.Now;
        var quota = new UsageSyncQuotaSnapshot
        {
            HasData = true,
            OpenCode = new UsageSyncOpenCodeQuota
            {
                Rolling = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddHours(2) },
                Weekly = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddDays(3) },
                Monthly = new UsageSyncOpenCodeQuotaWindow { ResetAt = now.AddDays(20) },
                Subscription = name,
            },
        };

        Assert.Equal(OpenCodeGoSubscriptionStatus.Renews, MainViewModel.CreateOpenCodeWebUsage(quota, now)?.Subscription);
    }

    // 새 문구는 4개 언어 모두 번역돼 있어야 한다(영어로 떨어지면 번역이 빠진 것).
    [Theory]
    [InlineData("ko")]
    [InlineData("zh")]
    [InlineData("ja")]
    public void NewStrings_AreTranslated(string lang)
    {
        static string[] Read() =>
        [
            Loc.OpenCodeGoInactive(OpenCodeGoSubscriptionStatus.ApprovalNeeded),
            Loc.OpenCodeGoInactive(OpenCodeGoSubscriptionStatus.RenewalProcessing),
            Loc.OpenCodeGoInactive(OpenCodeGoSubscriptionStatus.ReauthorizationNeeded),
            Loc.OpenCodeGoInactive(OpenCodeGoSubscriptionStatus.Paused),
            Loc.OpenCodeGoSubscription(OpenCodeGoSubscriptionStatus.PaymentFailed, "10/19"),
            Loc.OpenCodeGoSubscription(OpenCodeGoSubscriptionStatus.ActionNeeded, "10/19"),
            Loc.OpenCodeGoSubscription(OpenCodeGoSubscriptionStatus.Ends, "10/19"),
            Loc.OpenCodeGoSubscription(OpenCodeGoSubscriptionStatus.SwitchesToGo, "10/19"),
            Loc.OpenCodeRollingWindow,
            Loc.OpenCodeWeeklyWindow,
            Loc.OpenCodeMonthlyWindow,
            Loc.OpenCodeLimitReached("W", "D"),
            Loc.OpenCodeLimitUsingCredits("1h"),
            Loc.OpenCodeLimitGoEnds("1h"),
            Loc.OpenCodeLimitResumes("1h"),
            Loc.OpenCodeExtraUsageOn,
            Loc.OpenCodeCredits("$1.00"),
        ];

        var english = InEnglish(Read);
        var translated = InLanguage(lang, Read);

        Assert.All(translated.Zip(english), pair => Assert.NotEqual(pair.Second, pair.First));
        Assert.All(translated, text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }
}
