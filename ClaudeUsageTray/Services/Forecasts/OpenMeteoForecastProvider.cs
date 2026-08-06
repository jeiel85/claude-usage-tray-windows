using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.Forecasts;

/// <summary>
/// Open-Meteo Forecast API. 모델을 지정하지 않으면 API 가 좌표별로 가장 해상도가 높은
/// 모델을 자동 선택한다(best_match).
/// </summary>
public class OpenMeteoForecastProvider : IForecastProvider
{
    public const string ProviderId = "open-meteo";

    /// <summary>모델 자동 선택. URL 에 models 파라미터를 붙이지 않는다.</summary>
    public const string AutoModel = "best_match";

    /// <summary>
    /// 설정 UI 에 노출할 모델 목록. 전부 2026-08-06 에 서울 좌표로 응답을 실측 확인했다.
    /// kma_seamless 는 KMA 가 2026년 3월 말 UM 계열 모델을 종료하고 KIM 으로 전환하면서
    /// Open-Meteo 측 수급이 끊겨 모든 값이 null 로 오므로 목록에서 제외한다.
    /// 수급이 복구되면 다시 추가할 것.
    /// </summary>
    public static readonly IReadOnlyList<string> SelectableModels =
    [
        AutoModel,
        "ecmwf_ifs025",
        "gfs_seamless",
        "icon_seamless",
        "ukmo_seamless",
        "jma_seamless",
        "metno_seamless"
    ];

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.WeatherTimeoutSeconds)
    };

    public string Id => ProviderId;

    public async Task<WeatherReport?> GetForecastAsync(
        WeatherLocation location, string? modelId, CancellationToken ct = default)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?" +
                  $"latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&current=temperature_2m,apparent_temperature,weather_code,precipitation,wind_speed_10m" +
                  $"&hourly=temperature_2m,precipitation_probability,weather_code" +
                  $"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  $"&timezone={Uri.EscapeDataString(location.Timezone)}" +
                  $"&forecast_days=3&format=json";

        if (!string.IsNullOrWhiteSpace(modelId) && modelId != AutoModel)
            url += $"&models={Uri.EscapeDataString(modelId)}";

        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        CurrentWeatherSnapshot? current = null;
        IReadOnlyList<DailyWeatherForecast> daily = Array.Empty<DailyWeatherForecast>();

        if (doc.RootElement.TryGetProperty("current", out var cur))
        {
            // 수급이 끊긴 모델은 HTTP 200 에 값만 null 로 돌려준다. 이 경우 리포트를
            // 만들지 않고 null 을 반환해 상위에서 다음 provider 로 폴백하게 한다.
            var temp = GetNullableDouble(cur, "temperature_2m");
            var wc = GetNullableInt(cur, "weather_code");

            if (temp.HasValue && wc.HasValue)
            {
                var updatedAt = DateTimeOffset.UtcNow;
                if (cur.TryGetProperty("time", out var timeEl) && timeEl.GetString() is string ts
                    && DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    updatedAt = parsed;

                current = new CurrentWeatherSnapshot(
                    location, updatedAt, temp.Value,
                    GetNullableDouble(cur, "apparent_temperature"),
                    wc.Value, WeatherService.MapWmoCodeToKey(wc.Value),
                    GetNullableDouble(cur, "precipitation"),
                    GetNullableDouble(cur, "wind_speed_10m"));
            }
        }

        if (current == null)
            return null;

        if (doc.RootElement.TryGetProperty("daily", out var dly))
        {
            var dates = ParseDateArray(dly, "time");
            var codes = ParseIntNullableArray(dly, "weather_code");
            var mins = ParseDoubleNullableArray(dly, "temperature_2m_min");
            var maxs = ParseDoubleNullableArray(dly, "temperature_2m_max");
            var precipProbs = ParseIntNullableArray(dly, "precipitation_probability_max");

            var count = Math.Min(dates.Length, Math.Min(codes.Length,
                Math.Min(mins.Length, Math.Min(maxs.Length, precipProbs.Length))));

            var list = new List<DailyWeatherForecast>(count);
            for (int i = 0; i < count; i++)
                list.Add(new DailyWeatherForecast(dates[i], codes[i] ?? 0, mins[i], maxs[i], precipProbs[i]));

            daily = list.AsReadOnly();
        }

        return new WeatherReport(current, daily, Array.Empty<WeatherAlertItem>(), ProviderId);
    }

    private static double? GetNullableDouble(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    private static int? GetNullableInt(JsonElement el, string key) =>
        el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static DateOnly[] ParseDateArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr)) return Array.Empty<DateOnly>();
        var list = new List<DateOnly>();
        foreach (var item in arr.EnumerateArray())
        {
            var s = item.GetString();
            if (s != null && DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                list.Add(d);
        }
        return list.ToArray();
    }

    private static double?[] ParseDoubleNullableArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr)) return Array.Empty<double?>();
        var list = new List<double?>();
        foreach (var item in arr.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Number ? item.GetDouble() : null);
        return list.ToArray();
    }

    private static int?[] ParseIntNullableArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr)) return Array.Empty<int?>();
        var list = new List<int?>();
        foreach (var item in arr.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Number ? item.GetInt32() : null);
        return list.ToArray();
    }
}
