using System;
using System.Linq;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Forecasts;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class WeatherServiceTests
{
    [Theory]
    [InlineData(0, "clear")]
    [InlineData(1, "mainly_clear")]
    [InlineData(2, "partly_cloudy")]
    [InlineData(3, "overcast")]
    [InlineData(45, "fog")]
    [InlineData(48, "fog")]
    [InlineData(51, "drizzle")]
    [InlineData(53, "drizzle")]
    [InlineData(55, "drizzle")]
    [InlineData(56, "freezing_drizzle")]
    [InlineData(57, "freezing_drizzle")]
    [InlineData(61, "rain")]
    [InlineData(63, "rain")]
    [InlineData(65, "rain")]
    [InlineData(66, "freezing_rain")]
    [InlineData(67, "freezing_rain")]
    [InlineData(71, "snow")]
    [InlineData(73, "snow")]
    [InlineData(75, "snow")]
    [InlineData(77, "snow_grains")]
    [InlineData(80, "rain_showers")]
    [InlineData(81, "rain_showers")]
    [InlineData(82, "rain_showers")]
    [InlineData(85, "snow_showers")]
    [InlineData(86, "snow_showers")]
    [InlineData(95, "thunderstorm")]
    [InlineData(96, "thunderstorm")]
    [InlineData(99, "thunderstorm")]
    [InlineData(200, "unknown")]
    [InlineData(-1, "unknown")]
    public void MapWmoCodeToKey_ReturnsCorrectKey(int wmoCode, string expectedKey)
    {
        var key = WeatherService.MapWmoCodeToKey(wmoCode);
        Assert.Equal(expectedKey, key);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(45, false)]
    [InlineData(51, true)]
    [InlineData(61, true)]
    [InlineData(71, true)]
    [InlineData(80, true)]
    [InlineData(95, true)]
    [InlineData(99, true)]
    public void IsSignificantWeather_ReturnsCorrectValue(int wmoCode, bool expected)
    {
        var result = WeatherService.IsSignificantWeather(wmoCode);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetForecastAsync_ParsesRealApiResponse()
    {
        // Uses the public Open-Meteo API to test real-world parsing
        var service = new WeatherService();
        var location = new ClaudeUsageTray.Models.WeatherLocation(
            "Seoul", "KR", "Seoul", 37.5665, 126.9780, "Asia/Seoul");

        var report = await service.GetForecastAsync(location);

        Assert.NotNull(report);
        Assert.NotNull(report.Current);
        Assert.True(report.Daily.Count > 0);
        Assert.Equal("Seoul", report.Current.Location.Name);
        Assert.True(report.Current.TemperatureC > -50 && report.Current.TemperatureC < 60);
    }

    [Fact]
    public async Task SearchLocationsAsync_ReturnsResults_ForKnownCity()
    {
        var service = new WeatherService();
        var results = await service.SearchLocationsAsync("Seoul", "en");

        Assert.NotEmpty(results);
        var first = results.First();
        Assert.Equal("Seoul", first.Name);
        Assert.Equal("KR", first.CountryCode);
        Assert.True(first.Latitude > 30 && first.Latitude < 40);
        Assert.True(first.Longitude > 120 && first.Longitude < 130);
    }

    [Fact]
    public async Task SearchLocationsAsync_ReturnsEmpty_ForTooShortQuery()
    {
        var service = new WeatherService();
        var results = await service.SearchLocationsAsync("X", "en");
        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchLocationsAsync_ReturnsEmpty_ForNonsenseQuery()
    {
        var service = new WeatherService();
        var results = await service.SearchLocationsAsync("zxcvbnmasdfghj", "en");
        Assert.Empty(results);
    }

    // ===== 신규 소스 실제 응답 파싱 (v1.37.0) =====

    private static readonly ClaudeUsageTray.Models.WeatherLocation Seoul =
        new("Seoul", "KR", "Seoul", 37.5665, 126.9780, "Asia/Seoul");

    /// <summary>선택 가능한 모든 Open-Meteo 모델이 실제로 값을 돌려주는지 확인한다.</summary>
    [Theory]
    [InlineData("best_match")]
    [InlineData("ecmwf_ifs025")]
    [InlineData("gfs_seamless")]
    [InlineData("icon_seamless")]
    [InlineData("ukmo_seamless")]
    [InlineData("jma_seamless")]
    [InlineData("metno_seamless")]
    public async Task OpenMeteoProvider_ReturnsData_ForEverySelectableModel(string model)
    {
        var provider = new OpenMeteoForecastProvider();

        var report = await provider.GetForecastAsync(Seoul, model);

        Assert.NotNull(report);
        Assert.NotNull(report.Current);
        Assert.Equal(OpenMeteoForecastProvider.ProviderId, report.SourceId);
        Assert.InRange(report.Current.TemperatureC, -60, 60);
        Assert.NotEmpty(report.Daily);
    }

    /// <summary>
    /// KMA 는 HTTP 200 에 값만 null 로 돌려준다. 이 경우 예외가 아니라 null 을 반환해
    /// 오케스트레이터가 폴백할 수 있어야 한다.
    /// </summary>
    [Fact]
    public async Task OpenMeteoProvider_ReturnsNull_ForModelWithNoData()
    {
        var provider = new OpenMeteoForecastProvider();

        var report = await provider.GetForecastAsync(Seoul, "kma_seamless");

        Assert.Null(report);
    }

    [Fact]
    public async Task MetNorwayProvider_ParsesRealApiResponse()
    {
        var provider = new MetNorwayForecastProvider();

        var report = await provider.GetForecastAsync(Seoul, null);

        Assert.NotNull(report);
        Assert.NotNull(report.Current);
        Assert.Equal(MetNorwayForecastProvider.ProviderId, report.SourceId);
        Assert.InRange(report.Current.TemperatureC, -60, 60);
        Assert.Equal("Seoul", report.Current.Location.Name);

        // 일별 예보는 위치 시간대 기준으로 묶여 날짜가 연속해야 한다.
        Assert.NotEmpty(report.Daily);
        for (int i = 1; i < report.Daily.Count; i++)
            Assert.True(report.Daily[i].Date > report.Daily[i - 1].Date);

        foreach (var day in report.Daily)
        {
            Assert.True(day.MinTemperatureC <= day.MaxTemperatureC);
            // 이 API 는 강수확률을 제공하지 않는다 — 없는 값을 지어내지 않는지 확인한다.
            Assert.Null(day.PrecipitationProbabilityMax);
        }
    }

    /// <summary>어느 소스도 지정하지 않은 기본 경로가 살아 있는지 확인한다.</summary>
    [Fact]
    public async Task GetForecastAsync_ReportsWhichSourceAnswered()
    {
        var service = new WeatherService();

        var report = await service.GetForecastAsync(Seoul);

        Assert.NotNull(report.Current);
        Assert.Contains(report.SourceId, WeatherService.SelectableSources);
    }
}
