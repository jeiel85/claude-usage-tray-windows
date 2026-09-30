using System.Globalization;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 날씨 알림 중복 방지 키가 PC 의 지역 설정에 따라 달라져, 같은 위치·같은 날인데도 다른 PC 와의 중복을 못 걸러내던 문제(#176 리뷰).
// 실측: de-DE/fr-FR/sv-SE 는 좌표가 "37,57", th-TH 는 연도가 불교력(2569), ar-SA 는 히즈라력과 다른 숫자, sv-SE 는 음수 부호가 U+2212.
public class WeatherAlertKeysTests
{
    // 날짜는 시각 12:00(UTC+0) 고정 — 어느 시간대에서 실행해도 같은 날짜 문자열이 나온다.
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const double Latitude = 37.5665;
    private const double Longitude = 126.978;

    private static string[] AllKeys() =>
    [
        WeatherAlertKeys.Daily(Now, Latitude, Longitude),
        WeatherAlertKeys.Condition("rain", WeatherAlertKeys.Day(Now), 70, Latitude, Longitude),
        WeatherAlertKeys.Condition("heat", WeatherAlertKeys.Day(Now), 33, Latitude, Longitude),
        WeatherAlertKeys.Condition("cold", WeatherAlertKeys.Day(Now), -10, Latitude, Longitude),
        WeatherAlertKeys.Condition("wind", WeatherAlertKeys.WindWindow(Now, 6), 50, Latitude, Longitude),
    ];

    private static T WithCulture<T>(CultureInfo culture, Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    // 평소 환경에서 만들던 키와 글자 하나 다르지 않아야 한다 — 이미 디스크에 남은 발송 기록(AlertCache)이 계속 유효하다.
    [Fact]
    public void Keys_KeepTheExistingFormat()
    {
        var keys = WithCulture(CultureInfo.InvariantCulture, AllKeys);

        Assert.Equal(new[]
        {
            "daily:20260930:37.57:126.98",
            "condition:rain:20260930:70:37.57:126.98",
            "condition:heat:20260930:33:37.57:126.98",
            "condition:cold:20260930:-10:37.57:126.98",
            "condition:wind:20260930-2:50:37.57:126.98",
        }, keys);
    }

    // WeatherAlertCacheTests 가 쓰는 실제 기록 형태와도 같아야 한다.
    [Fact]
    public void Keys_MatchTheSamplesStoredInTheAlertCache()
    {
        var day = new DateTimeOffset(2026, 8, 1, 7, 30, 0, TimeSpan.FromHours(9));

        Assert.Equal("daily:20260801:37.48:126.89", WeatherAlertKeys.Daily(day, 37.48, 126.89));
        Assert.Equal("condition:heat:2026080114:33:37.48:126.89",
            WeatherAlertKeys.Condition("heat", "2026080114", 33, 37.48, 126.89));
    }

    [Theory]
    [InlineData("ko-KR")]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("th-TH")]
    [InlineData("ar-SA")]
    [InlineData("sv-SE")]
    [InlineData("fa-IR")]
    public void Keys_DoNotDependOnTheCurrentCulture(string culture)
    {
        var expected = WithCulture(CultureInfo.InvariantCulture, AllKeys);

        var actual = WithCulture(CultureInfo.GetCultureInfo(culture), AllKeys);

        Assert.Equal(expected, actual);
    }

    // 같은 위치·같은 날이면 지역 설정이 달라도 같은 ntfy 사건 키가 나와야 다른 PC 와의 중복이 걸러진다.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    [InlineData("sv-SE")]
    public void TwoPcsWithDifferentRegionalSettings_ProduceTheSameNtfyEvent(string otherCulture)
    {
        var pcA = WithCulture(CultureInfo.GetCultureInfo("ko-KR"),
            () => NtfyEventKey.ForExact("weather", WeatherAlertKeys.Daily(Now, Latitude, Longitude)));
        var pcB = WithCulture(CultureInfo.GetCultureInfo(otherCulture),
            () => NtfyEventKey.ForExact("weather", WeatherAlertKeys.Daily(Now, Latitude, Longitude)));

        Assert.Equal(pcA.SequenceId, pcB.SequenceId);
        Assert.True(pcB.Matches(pcA.SequenceId));
    }

    [Fact]
    public void DifferentDaysOrPlaces_AreDifferentKeys()
    {
        var today = WeatherAlertKeys.Daily(Now, Latitude, Longitude);

        Assert.NotEqual(today, WeatherAlertKeys.Daily(Now.AddDays(1), Latitude, Longitude));
        Assert.NotEqual(today, WeatherAlertKeys.Daily(Now, 35.1796, 129.0756));
    }
}
