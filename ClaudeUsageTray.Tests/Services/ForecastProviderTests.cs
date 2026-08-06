using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Forecasts;
using ClaudeUsageTray.Services.WeatherWarnings;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class ForecastProviderTests
{
    private static readonly WeatherLocation Seoul =
        new("Seoul", "KR", "Seoul", 37.5665, 126.9780, "Asia/Seoul");

    // ===== provider 선택과 폴백 =====

    /// <summary>
    /// 데이터가 없는 소스(예: 수급이 끊긴 모델)를 골랐어도 날씨가 비지 않아야 한다.
    /// </summary>
    [Fact]
    public async Task GetForecastAsync_FallsBackToNextProvider_WhenFirstReturnsNull()
    {
        var empty = new StubForecastProvider("empty", _ => null);
        var working = new StubForecastProvider("working", loc => Report(loc, "working"));
        var service = new WeatherService([empty, working]);

        var report = await service.GetForecastAsync(Seoul, "empty", null);

        Assert.Equal("working", report.SourceId);
        Assert.True(empty.CallCount == 1 && working.CallCount == 1);
    }

    [Fact]
    public async Task GetForecastAsync_FallsBackToNextProvider_WhenFirstThrows()
    {
        var broken = new StubForecastProvider("broken",
            _ => throw new InvalidOperationException("network down"));
        var working = new StubForecastProvider("working", loc => Report(loc, "working"));
        var service = new WeatherService([broken, working]);

        var report = await service.GetForecastAsync(Seoul, WeatherService.AutoSource, null);

        Assert.Equal("working", report.SourceId);
    }

    [Fact]
    public async Task GetForecastAsync_PrefersSelectedSource_OverListOrder()
    {
        var first = new StubForecastProvider("first", loc => Report(loc, "first"));
        var second = new StubForecastProvider("second", loc => Report(loc, "second"));
        var service = new WeatherService([first, second]);

        var report = await service.GetForecastAsync(Seoul, "second", null);

        Assert.Equal("second", report.SourceId);
        Assert.Equal(0, first.CallCount);
    }

    [Fact]
    public async Task GetForecastAsync_TreatsUnknownSourceAsAuto()
    {
        var first = new StubForecastProvider("first", loc => Report(loc, "first"));
        var service = new WeatherService([first]);

        var report = await service.GetForecastAsync(Seoul, "no-such-provider", null);

        Assert.Equal("first", report.SourceId);
    }

    /// <summary>
    /// 모델 이름은 provider 마다 어휘가 달라, 폴백으로 넘어간 provider 에 그대로 넘기면
    /// 엉뚱한 요청이 된다.
    /// </summary>
    [Fact]
    public async Task GetForecastAsync_DoesNotPassModelToFallbackProvider()
    {
        var empty = new StubForecastProvider("empty", _ => null);
        var fallback = new StubForecastProvider("fallback", loc => Report(loc, "fallback"));
        var service = new WeatherService([empty, fallback]);

        await service.GetForecastAsync(Seoul, "empty", "ecmwf_ifs025");

        Assert.Equal("ecmwf_ifs025", empty.LastModelId);
        Assert.Null(fallback.LastModelId);
    }

    [Fact]
    public async Task GetForecastAsync_Throws_WhenEveryProviderFails()
    {
        var service = new WeatherService([new StubForecastProvider("empty", _ => null)]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetForecastAsync(Seoul, WeatherService.AutoSource, null));
    }

    [Fact]
    public void SelectableSources_ContainAutoAndBothProviders()
    {
        Assert.Contains(WeatherService.AutoSource, WeatherService.SelectableSources);
        Assert.Contains(OpenMeteoForecastProvider.ProviderId, WeatherService.SelectableSources);
        Assert.Contains(MetNorwayForecastProvider.ProviderId, WeatherService.SelectableSources);
    }

    /// <summary>
    /// KMA 는 2026년 3월 UM→KIM 전환 이후 Open-Meteo 로 값을 주지 못한다(모든 필드 null).
    /// 목록에 남아 있으면 사용자가 고를 수 있고, 고르면 매번 폴백을 타게 된다.
    /// </summary>
    [Fact]
    public void SelectableModels_ExcludeDiscontinuedKmaModel()
    {
        Assert.DoesNotContain("kma_seamless", OpenMeteoForecastProvider.SelectableModels);
        Assert.Equal(OpenMeteoForecastProvider.AutoModel, OpenMeteoForecastProvider.SelectableModels[0]);
    }

    // ===== MET Norway symbol_code 매핑 =====

    [Theory]
    [InlineData("clearsky_day", 0)]
    [InlineData("clearsky_night", 0)]
    [InlineData("clearsky_polartwilight", 0)]
    [InlineData("fair_day", 1)]
    [InlineData("partlycloudy_night", 2)]
    [InlineData("cloudy", 3)]
    [InlineData("fog", 45)]
    [InlineData("lightrain", 61)]
    [InlineData("rain", 63)]
    [InlineData("heavyrain", 65)]
    [InlineData("lightrainshowers_day", 80)]
    [InlineData("rainshowers_day", 81)]
    [InlineData("heavyrainshowers_night", 82)]
    [InlineData("lightsnow", 71)]
    [InlineData("snow", 73)]
    [InlineData("heavysnow", 75)]
    [InlineData("snowshowers_day", 85)]
    [InlineData("heavysnowshowers_day", 86)]
    [InlineData("lightsleet", 66)]
    [InlineData("heavysleet", 67)]
    [InlineData("rainandthunder", 95)]
    [InlineData("heavysleetshowersandthunder_day", 95)]
    [InlineData("snowandthunder", 95)]
    public void MapSymbolToWmoCode_MapsKnownSymbols(string symbol, int expected)
    {
        Assert.Equal(expected, MetNorwayForecastProvider.MapSymbolToWmoCode(symbol));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("some_future_symbol")]
    public void MapSymbolToWmoCode_FallsBackToClear_ForUnknownInput(string? symbol)
    {
        Assert.Equal(0, MetNorwayForecastProvider.MapSymbolToWmoCode(symbol));
    }

    /// <summary>매핑 결과가 앱 내부 condition key 로 온전히 변환되는지 확인한다.</summary>
    [Fact]
    public void MapSymbolToWmoCode_ProducesKnownConditionKeys()
    {
        string[] symbols =
        [
            "clearsky_day", "fair_day", "partlycloudy_day", "cloudy", "fog",
            "lightrain", "rain", "heavyrain", "rainshowers_day",
            "lightsnow", "snow", "snowshowers_day", "sleet", "rainandthunder"
        ];

        foreach (var symbol in symbols)
        {
            var key = WeatherService.MapWmoCodeToKey(
                MetNorwayForecastProvider.MapSymbolToWmoCode(symbol));
            Assert.NotEqual("unknown", key);
        }
    }

    // ===== JMA 좌표 → 기상대 매핑 =====

    [Theory]
    [InlineData(35.6895, 139.6917, "130000")] // 東京
    [InlineData(34.6863, 135.5200, "270000")] // 大阪
    [InlineData(43.0642, 141.3469, "016000")] // 札幌（石狩）
    [InlineData(26.2124, 127.6809, "471000")] // 那覇（沖縄本島）
    [InlineData(24.3448, 124.1572, "474000")] // 石垣（八重山）
    [InlineData(43.7708, 142.3650, "012000")] // 旭川（上川・留萌）
    [InlineData(33.5597, 133.5311, "390000")] // 高知
    [InlineData(28.3778, 129.4936, "460040")] // 奄美
    public void FindNearestOffice_ResolvesMajorCities(double lat, double lon, string expected)
    {
        Assert.Equal(expected, JmaWeatherWarningProvider.FindNearestOffice(lat, lon));
    }

    // ===== JMA 특보 등급 =====

    [Theory]
    [InlineData("03", "Severe")]  // 大雨警報
    [InlineData("05", "Severe")]  // 暴風警報
    [InlineData("07", "Severe")]  // 波浪警報
    public void Classify_ReportsWarningsAsSevere(string code, string expectedSeverity)
    {
        var (name, severity) = JmaWeatherWarningProvider.Classify(code);

        Assert.NotNull(name);
        Assert.Equal(expectedSeverity, severity);
    }

    [Theory]
    [InlineData("33")]  // 大雨特別警報
    [InlineData("35")]  // 暴風特別警報
    public void Classify_ReportsEmergencyWarningsAsExtreme(string code)
    {
        var (name, severity) = JmaWeatherWarningProvider.Classify(code);

        Assert.NotNull(name);
        Assert.Equal("Extreme", severity);
    }

    /// <summary>
    /// 한 관할에 注意報 가 100건 넘게 걸리는 일이 흔하다. 이걸 그대로 알리면 스팸이 된다.
    /// </summary>
    [Theory]
    [InlineData("14")]  // 雷注意報 — 실측 확인된 코드
    [InlineData("15")]  // 強風注意報
    [InlineData("20")]  // 濃霧注意報
    [InlineData("99")]  // 미지의 코드
    public void Classify_SuppressesAdvisories(string code)
    {
        var (name, _) = JmaWeatherWarningProvider.Classify(code);
        Assert.Null(name);
    }

    [Fact]
    public void JmaProvider_SupportsJapanOnly()
    {
        var provider = new JmaWeatherWarningProvider();

        Assert.True(provider.Supports(new WeatherLocation("Tokyo", "JP", null, 35.68, 139.69, "Asia/Tokyo")));
        Assert.True(provider.Supports(new WeatherLocation("Tokyo", "jp", null, 35.68, 139.69, "Asia/Tokyo")));
        Assert.False(provider.Supports(Seoul));
    }

    // ===== 테스트 더블 =====

    private static WeatherReport Report(WeatherLocation location, string sourceId) =>
        new(new CurrentWeatherSnapshot(location, DateTimeOffset.UtcNow, 20, null, 0, "clear", null, null),
            Array.Empty<DailyWeatherForecast>(), Array.Empty<WeatherAlertItem>(), sourceId);

    private sealed class StubForecastProvider(
        string id, Func<WeatherLocation, WeatherReport?> handler) : IForecastProvider
    {
        public string Id { get; } = id;
        public int CallCount { get; private set; }
        public string? LastModelId { get; private set; }

        public Task<WeatherReport?> GetForecastAsync(
            WeatherLocation location, string? modelId, CancellationToken ct = default)
        {
            CallCount++;
            LastModelId = modelId;
            return Task.FromResult(handler(location));
        }
    }
}
