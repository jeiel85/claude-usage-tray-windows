using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.Forecasts;

/// <summary>
/// MET Norway Locationforecast 2.0. API 키가 없는 대신 식별 가능한 User-Agent 가 필수이며,
/// 없으면 403 이 돌아온다.
/// </summary>
/// <remarks>
/// 이 API 는 강수확률을 제공하지 않는다(compact/complete 모두 <c>precipitation_amount</c> 만 있다).
/// 따라서 이 소스로 폴백된 동안에는 강수확률 임계값 기반 조건 알림이 발송되지 않는다.
/// 기온·풍속 기반 알림(폭염/한파/강풍)은 정상 동작한다.
/// </remarks>
public class MetNorwayForecastProvider : IForecastProvider
{
    public const string ProviderId = "met-norway";

    private const int ForecastDays = 3;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.WeatherTimeoutSeconds)
    };

    static MetNorwayForecastProvider()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"ClaudeUsageTray/{UpdateService.CurrentVersion.ToString(3)} " +
            "(https://github.com/jeiel85/claude-usage-tray-windows)");
    }

    public string Id => ProviderId;

    public async Task<WeatherReport?> GetForecastAsync(
        WeatherLocation location, string? modelId, CancellationToken ct = default)
    {
        // modelId 는 무시한다 — 이 API 는 모델 선택을 제공하지 않는다.
        var url = "https://api.met.no/weatherapi/locationforecast/2.0/complete?" +
                  $"lat={location.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&lon={location.Longitude.ToString(CultureInfo.InvariantCulture)}";

        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("properties", out var props)
            || !props.TryGetProperty("timeseries", out var series)
            || series.ValueKind != JsonValueKind.Array
            || series.GetArrayLength() == 0)
            return null;

        var tz = ResolveTimeZone(location.Timezone);
        var entries = new List<Entry>();

        foreach (var item in series.EnumerateArray())
        {
            if (!item.TryGetProperty("time", out var timeEl)
                || timeEl.GetString() is not string ts
                || !DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture,
                       DateTimeStyles.AdjustToUniversal, out var utc))
                continue;

            if (!item.TryGetProperty("data", out var data)) continue;

            double? temp = null, wind = null;
            if (data.TryGetProperty("instant", out var instant)
                && instant.TryGetProperty("details", out var details))
            {
                temp = GetNullableDouble(details, "air_temperature");
                // m/s → km/h. 앱의 강풍 임계값은 km/h 기준이다.
                var ws = GetNullableDouble(details, "wind_speed");
                wind = ws.HasValue ? ws.Value * 3.6 : null;
            }

            if (!temp.HasValue) continue;

            var (symbol, precip, dayMax, dayMin) = ReadPeriods(data);

            entries.Add(new Entry(
                TimeZoneInfo.ConvertTime(utc, tz),
                temp.Value, wind, symbol, precip, dayMax, dayMin));
        }

        if (entries.Count == 0) return null;

        var first = entries[0];
        var currentCode = MapSymbolToWmoCode(first.SymbolCode);
        var current = new CurrentWeatherSnapshot(
            location, first.LocalTime, first.TemperatureC,
            ApparentTemperatureC: null,   // 이 API 는 체감온도를 제공하지 않는다
            currentCode, WeatherService.MapWmoCodeToKey(currentCode),
            first.PrecipitationMm, first.WindSpeedKmh);

        return new WeatherReport(current, BuildDaily(entries),
            Array.Empty<WeatherAlertItem>(), ProviderId);
    }

    private static IReadOnlyList<DailyWeatherForecast> BuildDaily(List<Entry> entries)
    {
        var daily = new List<DailyWeatherForecast>();

        foreach (var group in entries.GroupBy(e => DateOnly.FromDateTime(e.LocalTime.DateTime))
                                     .OrderBy(g => g.Key)
                                     .Take(ForecastDays))
        {
            double max = double.MinValue, min = double.MaxValue;
            var worstCode = 0;

            foreach (var e in group)
            {
                max = Math.Max(max, e.TemperatureC);
                min = Math.Min(min, e.TemperatureC);

                // 6시간 구간 요약이 붙어 있으면 그 구간의 극값도 반영한다.
                if (e.PeriodMaxC.HasValue) max = Math.Max(max, e.PeriodMaxC.Value);
                if (e.PeriodMinC.HasValue) min = Math.Min(min, e.PeriodMinC.Value);

                // 하루의 대표 상태로 가장 유의한(코드값이 큰) 현상을 고른다.
                // WMO 코드는 대체로 심각도 순으로 증가하므로 강수/뇌우가 맑음을 이긴다.
                var code = MapSymbolToWmoCode(e.SymbolCode);
                if (code > worstCode) worstCode = code;
            }

            daily.Add(new DailyWeatherForecast(
                group.Key, worstCode, min, max,
                // 이 API 는 강수확률을 제공하지 않는다.
                PrecipitationProbabilityMax: null));
        }

        return daily.AsReadOnly();
    }

    private static (string? symbol, double? precip, double? max, double? min) ReadPeriods(JsonElement data)
    {
        string? symbol = null;
        double? precip = null, max = null, min = null;

        foreach (var key in new[] { "next_1_hours", "next_6_hours", "next_12_hours" })
        {
            if (!data.TryGetProperty(key, out var period)) continue;

            if (symbol == null
                && period.TryGetProperty("summary", out var summary)
                && summary.TryGetProperty("symbol_code", out var sc))
                symbol = sc.GetString();

            if (period.TryGetProperty("details", out var pd))
            {
                precip ??= GetNullableDouble(pd, "precipitation_amount");
                max ??= GetNullableDouble(pd, "air_temperature_max");
                min ??= GetNullableDouble(pd, "air_temperature_min");
            }
        }

        return (symbol, precip, max, min);
    }

    /// <summary>
    /// MET Norway 의 symbol_code 를 앱 내부 표준인 WMO 코드로 옮긴다.
    /// <c>_day</c> / <c>_night</c> / <c>_polartwilight</c> 접미사는 주야 구분일 뿐이라 버린다.
    /// </summary>
    internal static int MapSymbolToWmoCode(string? symbolCode)
    {
        if (string.IsNullOrWhiteSpace(symbolCode)) return 0;

        var s = symbolCode.Trim().ToLowerInvariant();
        foreach (var suffix in new[] { "_day", "_night", "_polartwilight" })
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                s = s[..^suffix.Length];
                break;
            }
        }

        // 뇌우 변형(rainandthunder, heavysleetshowersandthunder, …)은 모두 뇌우로 본다.
        if (s.Contains("thunder", StringComparison.Ordinal)) return 95;

        return s switch
        {
            "clearsky" => 0,
            "fair" => 1,
            "partlycloudy" => 2,
            "cloudy" => 3,
            "fog" => 45,

            "lightrain" => 61,
            "rain" => 63,
            "heavyrain" => 65,
            "lightrainshowers" => 80,
            "rainshowers" => 81,
            "heavyrainshowers" => 82,

            // 진눈깨비는 WMO 에 정확한 대응이 없어 어는비(66/67)로 옮긴다.
            // 앱 표시상으로는 두 코드 모두 "비"로 묶인다.
            "lightsleet" or "lightsleetshowers" => 66,
            "sleet" or "heavysleet" or "sleetshowers" or "heavysleetshowers" => 67,

            "lightsnow" => 71,
            "snow" => 73,
            "heavysnow" => 75,
            "lightsnowshowers" or "snowshowers" => 85,
            "heavysnowshowers" => 86,

            _ => 0
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone)
            || string.Equals(timezone, "auto", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Local;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }

    private static double? GetNullableDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private readonly record struct Entry(
        DateTimeOffset LocalTime,
        double TemperatureC,
        double? WindSpeedKmh,
        string? SymbolCode,
        double? PrecipitationMm,
        double? PeriodMaxC,
        double? PeriodMinC);
}
