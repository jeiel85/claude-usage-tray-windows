using System;
using System.Linq;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Forecasts;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 실제 날씨 API(Open-Meteo·MET Norway)를 부르는 테스트.
/// 응답 형식이 바뀌었는지 알려 주는 유일한 신호라 지우지 않지만, 결과가 러너의 네트워크 상태에 달려 있어
/// 릴리스 게이트에서는 빼고 따로 돌린다(<c>Category=Network</c>, #238). GitHub 러너에서 Open-Meteo 로의
/// TLS 연결이 끊겨 v1.56.0 은 릴리스되지 못했고 v1.57.x 는 재실행으로 통과시켰다.
/// 로컬에서는 기본 <c>dotnet test</c> 에 함께 돈다.
/// </summary>
[Trait("Category", "Network")]
public class WeatherLiveApiTests
{
    private static readonly ClaudeUsageTray.Models.WeatherLocation Seoul =
        new("Seoul", "KR", "Seoul", 37.5665, 126.9780, "Asia/Seoul");

    [Fact]
    public async Task GetForecastAsync_ParsesRealApiResponse()
    {
        var service = new WeatherService();

        var report = await service.GetForecastAsync(Seoul);

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
    public async Task SearchLocationsAsync_ReturnsEmpty_ForNonsenseQuery()
    {
        var service = new WeatherService();
        var results = await service.SearchLocationsAsync("zxcvbnmasdfghj", "en");
        Assert.Empty(results);
    }

    /// <summary>선택 가능한 모든 Open-Meteo 모델이 실제로 값을 돌려주는지 확인한다(v1.37.0).</summary>
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
