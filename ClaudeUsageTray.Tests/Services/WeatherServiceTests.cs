using System;
using System.Linq;
using ClaudeUsageTray.Services;
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
}
