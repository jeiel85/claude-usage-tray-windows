using System.Globalization;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 날씨 알림 중복 방지 키가 PC 마다 달라지는 값(지역 설정 서식, 시간대)에 좌우돼, 같은 위치·같은 날인데도 다른 PC 와의 중복을 못 걸러내거나
// 반대로 다른 날의 알림을 같은 사건으로 착각해 삼키던 문제(#176 리뷰).
// 실측: de-DE/fr-FR/sv-SE 는 좌표가 "37,57", th-TH 는 연도가 불교력(2569), ar-SA 는 히즈라력과 다른 숫자, sv-SE 는 음수 부호가 U+2212.
public class WeatherAlertKeysTests
{
    // 예보 날짜(위치의 현지 날짜)와 관측 시각은 고정값 — 어느 시간대에서 실행해도 같은 문자열이 나온다.
    private static readonly DateOnly ForecastDate = new(2026, 9, 30);
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private const double Latitude = 37.5665;
    private const double Longitude = 126.978;

    private static string[] AllKeys() =>
    [
        WeatherAlertKeys.Daily(ForecastDate, Latitude, Longitude),
        WeatherAlertKeys.Condition("rain", WeatherAlertKeys.Day(ForecastDate), 70, Latitude, Longitude),
        WeatherAlertKeys.Condition("heat", WeatherAlertKeys.Day(ForecastDate), 33, Latitude, Longitude),
        WeatherAlertKeys.Condition("cold", WeatherAlertKeys.Day(ForecastDate), -10, Latitude, Longitude),
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
        Assert.Equal("daily:20260801:37.48:126.89", WeatherAlertKeys.Daily(new DateOnly(2026, 8, 1), 37.48, 126.89));
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

    // 같은 위치·같은 예보 날짜면 지역 설정이 달라도 같은 ntfy 사건 키가 나와야 다른 PC 와의 중복이 걸러진다.
    [Theory]
    [InlineData("de-DE")]
    [InlineData("th-TH")]
    [InlineData("sv-SE")]
    public void TwoPcsWithDifferentRegionalSettings_ProduceTheSameNtfyEvent(string otherCulture)
    {
        var pcA = WithCulture(CultureInfo.GetCultureInfo("ko-KR"),
            () => NtfyEventKey.ForExact("weather", WeatherAlertKeys.Daily(ForecastDate, Latitude, Longitude)));
        var pcB = WithCulture(CultureInfo.GetCultureInfo(otherCulture),
            () => NtfyEventKey.ForExact("weather", WeatherAlertKeys.Daily(ForecastDate, Latitude, Longitude)));

        Assert.Equal(pcA.SequenceId, pcB.SequenceId);
        Assert.True(pcB.Matches(pcA.SequenceId));
    }

    [Fact]
    public void DifferentForecastDaysOrPlaces_AreDifferentKeys()
    {
        var today = WeatherAlertKeys.Daily(ForecastDate, Latitude, Longitude);

        Assert.NotEqual(today, WeatherAlertKeys.Daily(ForecastDate.AddDays(1), Latitude, Longitude));
        Assert.NotEqual(today, WeatherAlertKeys.Daily(ForecastDate, 35.1796, 129.0756));
    }

    // Codex 리뷰봇 지적(#176): PC 의 오늘이 아니라 예보 자체의 날짜(위치의 현지 날짜)로 키를 만들어야 한다.
    // 도쿄 PC 가 LA 를 보면 도쿄가 9월 30일일 때 LA 예보는 9월 29일이다. 도쿄의 날짜를 쓰면 이 예보가 "9월 30일" 키로 나가고,
    // 뒤에 LA PC 가 보내는 LA 의 진짜 9월 30일 예보가 같은 키라서 막힌다.
    [Fact]
    public void ForecastDate_KeepsTwoDifferentLocalDaysApart()
    {
        const double laLat = 34.05, laLon = -118.24;
        var laSept29 = WeatherAlertKeys.Daily(new DateOnly(2026, 9, 29), laLat, laLon);   // 도쿄 PC 가 9월 30일 아침에 본 LA 의 예보
        var laSept30 = WeatherAlertKeys.Daily(new DateOnly(2026, 9, 30), laLat, laLon);   // LA PC 가 LA 의 9월 30일에 본 예보

        var tokyoPc = NtfyEventKey.ForExact("weather", laSept29);
        var laPc = NtfyEventKey.ForExact("weather", laSept30);

        Assert.False(laPc.Matches(tokyoPc.SequenceId));
    }

    // 같은 예보(같은 위치·같은 예보 날짜)는 PC 의 시간대와 무관하게 같은 사건이다 — 키에 PC 의 시각이 들어가지 않는다.
    [Fact]
    public void TheSameForecast_IsTheSameEvent_RegardlessOfTheMachine()
    {
        var fromTokyo = NtfyEventKey.ForExact("weather", WeatherAlertKeys.Daily(new DateOnly(2026, 9, 29), 34.05, -118.24));
        var fromLosAngeles = NtfyEventKey.ForExact("weather", WeatherAlertKeys.Daily(new DateOnly(2026, 9, 29), 34.05, -118.24));

        Assert.True(fromLosAngeles.Matches(fromTokyo.SequenceId));
    }

    // 강풍은 현재 관측으로 판정해 쿨다운 창을 시각으로 자른다. PC 의 로컬 시각으로 자르면 시간대가 다른 PC 에서
    // 16시간 떨어진 두 순간이 같은 창으로 찍힌다 — 절대 시각(UTC)으로 잘라야 한다.
    [Fact]
    public void WindWindow_UsesAnAbsoluteInstant_NotTheMachineClock()
    {
        var tokyo = new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.FromHours(9));       // = 09-29 20:00 UTC
        var losAngeles = new DateTimeOffset(2026, 9, 30, 3, 0, 0, TimeSpan.FromHours(-7));  // = 09-30 10:00 UTC (14시간 뒤)

        // 로컬 시각으로 자르면 둘 다 "20260930-0" 이 되어 LA 의 정당한 강풍 알림이 막혔다.
        Assert.NotEqual(WeatherAlertKeys.WindWindow(tokyo, 6), WeatherAlertKeys.WindWindow(losAngeles, 6));
        Assert.Equal("20260929-3", WeatherAlertKeys.WindWindow(tokyo, 6));
        Assert.Equal("20260930-1", WeatherAlertKeys.WindWindow(losAngeles, 6));
    }

    [Fact]
    public void WindWindow_IsTheSameForTheSameInstantInAnyOffset()
    {
        var utc = new DateTimeOffset(2026, 9, 30, 12, 30, 0, TimeSpan.Zero);
        var seoul = utc.ToOffset(TimeSpan.FromHours(9));
        var newYork = utc.ToOffset(TimeSpan.FromHours(-4));

        Assert.Equal(WeatherAlertKeys.WindWindow(utc, 6), WeatherAlertKeys.WindWindow(seoul, 6));
        Assert.Equal(WeatherAlertKeys.WindWindow(utc, 6), WeatherAlertKeys.WindWindow(newYork, 6));
    }

    // 같은 6시간 창 안의 관측은 같은 창이고, 다음 창으로 넘어가면 새 창이다(쿨다운 6시간).
    [Fact]
    public void WindWindow_ChangesEverySixHours()
    {
        var start = new DateTimeOffset(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);

        Assert.Equal(WeatherAlertKeys.WindWindow(start, 6), WeatherAlertKeys.WindWindow(start.AddHours(5).AddMinutes(59), 6));
        Assert.NotEqual(WeatherAlertKeys.WindWindow(start, 6), WeatherAlertKeys.WindWindow(start.AddHours(6), 6));
    }
}
