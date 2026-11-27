using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 크레딧 표시(#351). Claude <c>extra_usage</c> 는 금액을 통화 최소 단위로 주는데(2300 = $23.00) 예전에는
/// "2,300 크레딧" 으로 그렸고, 꺼져 있으면 줄을 숨겨 꺼진 이유가 보이지 않았다. Codex 는 <c>credits.balance</c> 를 읽지 않았다.
/// Loc 을 바꾸므로 Loc 을 쓰는 다른 테스트와 같은 컬렉션에서 직렬로 돈다.
/// </summary>
[Collection("WpfTests")]
public class CreditDisplayTests
{
    // 2026-11-27 실측 응답의 extra_usage 부분 그대로.
    private const string OutOfCreditsJson =
        """{"is_enabled":false,"monthly_limit":2300,"used_credits":0.0,"utilization":0.0,"currency":"USD","decimal_places":2,"disabled_reason":"out_of_credits","user_disabled":false,"spend_limit_reached":false,"credits_can_pay":"no","credits_ever_enabled":true,"daily":null,"weekly":null}""";

    private static void InLanguage(string lang, Action check)
    {
        var original = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);
            check();
        }
        finally
        {
            Loc.SetLanguage(original);
        }
    }

    private static ExtraUsage Parse(string json) => JsonSerializer.Deserialize<ExtraUsage>(json)!;

    [Fact]
    public void DisabledAccountThatUsedCreditsShowsMoneyAndReason() => InLanguage("ko", () =>
    {
        var extra = Parse(OutOfCreditsJson);

        Assert.True(ClaudeExtraUsageText.IsVisible(extra));
        Assert.Equal("$0.00 / $23.00 · 크레딧 없음", ClaudeExtraUsageText.Describe(extra));
    });

    [Fact]
    public void EnabledAccountShowsMoneyWithoutReason() => InLanguage("en", () =>
    {
        var extra = new ExtraUsage
        {
            IsEnabled = true, UsedCredits = 1234, MonthlyLimit = 5000, Currency = "USD", DecimalPlaces = 2,
        };

        Assert.Equal("$12.34 / $50.00", ClaudeExtraUsageText.Describe(extra));
    });

    [Theory]
    [InlineData(0, 2300, 0)]
    [InlineData(1150, 2300, 0.5)]
    [InlineData(5000, 2300, 1)]
    public void GaugeIsSpendOverMonthlyLimitEvenWhenDisabled(double used, double limit, double expected)
    {
        var extra = new ExtraUsage { IsEnabled = false, CreditsEverEnabled = true, UsedCredits = used, MonthlyLimit = limit, Currency = "USD" };

        Assert.Equal(expected, ClaudeExtraUsageText.Percent(extra), 3);
    }

    [Fact]
    public void GaugeIsTheSameWhenOnlyTheEnabledStateChanges()
    {
        // PR #352 리뷰: 켜진 쪽만 utilization 으로 계산해 같은 사용액이 50% → 0% 로 바뀌었다.
        var disabled = new ExtraUsage { IsEnabled = false, CreditsEverEnabled = true, UsedCredits = 1150, MonthlyLimit = 2300 };
        var enabled = new ExtraUsage { IsEnabled = true, UsedCredits = 1150, MonthlyLimit = 2300 };

        Assert.Equal(0.5, ClaudeExtraUsageText.Percent(disabled), 3);
        Assert.Equal(0.5, ClaudeExtraUsageText.Percent(enabled), 3);
    }

    [Fact]
    public void GaugeUsesServerUtilizationOnlyWhenSpendIsMissing()
    {
        Assert.Equal(0.25, ClaudeExtraUsageText.Percent(new ExtraUsage { IsEnabled = true, MonthlyLimit = 2300, Utilization = 25 }), 3);
        // 한도가 없으면 비율이 없다 — 예전처럼 0.
        Assert.Equal(0, ClaudeExtraUsageText.Percent(new ExtraUsage { IsEnabled = true, UsedCredits = 10, Utilization = 25 }));
        Assert.Equal(0, ClaudeExtraUsageText.Percent(null));
    }

    [Fact]
    public void AccountThatNeverEnabledExtraUsageShowsNothing()
    {
        var extra = new ExtraUsage { IsEnabled = false, MonthlyLimit = 2300, Currency = "USD", CreditsEverEnabled = false };

        Assert.False(ClaudeExtraUsageText.IsVisible(extra));
        Assert.Equal("", ClaudeExtraUsageText.Describe(extra));
        Assert.False(ClaudeExtraUsageText.IsVisible(null));
    }

    [Fact]
    public void ResponseWithoutCurrencyKeepsTheCreditCount() => InLanguage("en", () =>
    {
        var extra = new ExtraUsage { IsEnabled = true, UsedCredits = 150, MonthlyLimit = 1000 };

        Assert.Equal("150 / 1,000 credits", ClaudeExtraUsageText.Describe(extra));
    });

    [Fact]
    public void ReasonFollowsTheServerField() => InLanguage("en", () =>
    {
        var limitReached = new ExtraUsage { CreditsEverEnabled = true, SpendLimitReached = true, DisabledReason = "spend_limit" };
        var turnedOff = new ExtraUsage { CreditsEverEnabled = true, UserDisabled = true, DisabledReason = "user_disabled" };

        Assert.Equal("Limit reached", ClaudeExtraUsageText.DescribeDisabledReason(limitReached));
        Assert.Equal("Off", ClaudeExtraUsageText.DescribeDisabledReason(turnedOff));
    });

    [Fact]
    public void OtherCurrencyIsWrittenAfterTheNumber()
    {
        var extra = new ExtraUsage { Currency = "eur", DecimalPlaces = 2 };

        Assert.Equal("23.00 EUR", ClaudeExtraUsageText.FormatMoney(2300, extra));
    }

    [Theory]
    [InlineData("""{"credits":{"has_credits":false,"unlimited":false,"balance":"0"}}""", 0, false)]
    [InlineData("""{"credits":{"has_credits":true,"unlimited":false,"balance":"12.5"}}""", 12.5, false)]
    [InlineData("""{"credits":{"has_credits":true,"balance":40}}""", 40, false)]
    [InlineData("""{"credits":{"has_credits":true,"unlimited":true,"balance":null}}""", 0, true)]
    public void CodexCreditsAreReadFromWhamUsage(string json, double balance, bool unlimited)
    {
        using var doc = JsonDocument.Parse(json);

        var credits = CodexUsageMonitor.ParseCredits(doc.RootElement);

        Assert.NotNull(credits);
        Assert.Equal((decimal)balance, credits.Balance);
        Assert.Equal(unlimited, credits.Unlimited);
    }

    [Theory]
    [InlineData("""{"plan_type":"plus"}""")]
    [InlineData("""{"credits":null}""")]
    [InlineData("""{"credits":{"has_credits":false,"balance":"abc"}}""")]
    public void CodexCreditsAreNullWhenMissingOrUnreadable(string json)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Null(CodexUsageMonitor.ParseCredits(doc.RootElement));
    }

    [Fact]
    public void CodexCreditsLabel() => InLanguage("ko", () =>
    {
        Assert.Equal("0 크레딧", CodexViewModel.FormatCredits(new CodexCredits(0, false)));
        Assert.Equal("1,234.5 크레딧", CodexViewModel.FormatCredits(new CodexCredits(1234.5m, false)));
        Assert.Equal("무제한", CodexViewModel.FormatCredits(new CodexCredits(0, true)));
        Assert.Equal("", CodexViewModel.FormatCredits(null));
    });
}
