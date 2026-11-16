using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 사용량 알림 문구(#262). 예전 문구는 리셋 라벨의 " · " 에 또 " ·" 를 붙여 "· ·" 가 찍혔고,
/// 조기 소진 본문은 "(원래 초기화:  · 3h 후 초기화)" 로 깨졌다. 알림은 나중에 열어 보므로 절대 시각을 함께 적는다.
/// Loc 을 바꾸므로 Loc 을 쓰는 다른 테스트와 같은 컬렉션에서 직렬로 돈다.
/// </summary>
[Collection("WpfTests")]
public class AlertTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 14, 18, 0, TimeSpan.FromHours(9));

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

    [Fact]
    public void UsageAlertPutsServiceAndPercentFirstInTheTitle() => InLanguage("ko", () =>
    {
        // 잘려도 서비스와 퍼센트가 남게 앞에 둔다.
        Assert.Equal("Claude 90% · 5시간 한도", AlertText.UsageTitle("Claude", Loc.AlertWindowFiveHour, 90));
        Assert.Equal("Codex 75% · 사용량", AlertText.UsageTitle("Codex", Loc.AlertWindowUsage, 75));
        Assert.Equal("Claude 100% · 추가 사용량", AlertText.UsageTitle("Claude", Loc.AlertWindowExtra, 100));
    });

    [Fact]
    public void UsageBodyShowsTheResetClockAndTimeLeftWithoutDoubleSeparators() => InLanguage("ko", () =>
    {
        var resetAt = Now.AddHours(1).AddMinutes(12);
        var expectedClock = UsageCalculator.FormatClockStamp(resetAt, Now);
        var body = AlertText.UsageBody(90, resetAt, " · 1h 12m 후 초기화", Now);
        Assert.Equal($"{expectedClock} 초기화 · 1h 12m 남음", body);
        Assert.DoesNotContain("· ·", body);
    });

    [Fact]
    public void UsageBodyFallsBackToTheLabelThenToTheThreshold() => InLanguage("ko", () =>
    {
        // Codex 추정 리셋: 시각을 넘기지 않으므로 팝업 라벨에서 앞의 구분점만 뗀다.
        Assert.Equal("약 3h 0m 후 초기화(예상)", AlertText.UsageBody(90, null, " · 약 3h 0m 후 초기화(예상)", Now));
        // 이미 지난 리셋 시각은 쓰지 않는다.
        Assert.Equal("설정한 알림 기준 90%를 넘었습니다", AlertText.UsageBody(90, Now.AddMinutes(-1), "", Now));
        // 추가 사용량: 창이 없다.
        Assert.Equal("설정한 알림 기준 100%를 넘었습니다", AlertText.UsageBody(100, null, null, Now));
    });

    [Fact]
    public void EarlyExhaustionBodyNamesBothClockTimes() => InLanguage("ko", () =>
    {
        var depletion = Now.AddMinutes(42);
        var reset = Now.AddHours(2);
        var body = AlertText.EarlyExhaustionBody(depletion, reset, Now);
        Assert.Equal(
            $"지금 속도면 {UsageCalculator.FormatClockStamp(depletion, Now)}쯤 다 씁니다 · 초기화는 {UsageCalculator.FormatClockStamp(reset, Now)}",
            body);
    });

    [Theory]
    [InlineData("ko")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("en")]
    public void EveryLanguageHasItsOwnAlertText(string lang) => InLanguage(lang, () =>
    {
        var title = AlertText.UsageTitle("Claude", Loc.AlertWindowFiveHour, 90);
        Assert.Contains("Claude", title);
        Assert.Contains("90", title);
        Assert.Contains("1h 12m", AlertText.UsageBody(90, Now.AddHours(1).AddMinutes(12), null, Now));
        Assert.False(string.IsNullOrWhiteSpace(Loc.RateLimitAlertBody));
        Assert.False(string.IsNullOrWhiteSpace(Loc.EarlyExhaustionAlertTitle));
        if (lang != "en")
        {
            // 영어 문구가 그대로 새지 않았는지(번역 누락) 확인한다.
            InLanguage("en", () => Assert.NotEqual(AlertText.UsageTitle("Claude", Loc.AlertWindowFiveHour, 90), title));
        }
    });
}
