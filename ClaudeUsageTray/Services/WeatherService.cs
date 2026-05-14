using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class WeatherService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.WeatherTimeoutSeconds)
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<WeatherLocation>> SearchLocationsAsync(string query, string language)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return Array.Empty<WeatherLocation>();

        var lang = language switch
        {
            "ko" => "ko",
            "zh" => "zh",
            "ja" => "ja",
            _ => "en"
        };

        var url = $"https://geocoding-api.open-meteo.com/v1/search?" +
                  $"name={Uri.EscapeDataString(query)}&count=5&language={lang}&format=json";

        var json = await Http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("results", out var results))
            return Array.Empty<WeatherLocation>();

        var locations = new List<WeatherLocation>();
        foreach (var r in results.EnumerateArray())
        {
            var name = r.GetProperty("name").GetString() ?? "";
            var country = r.GetProperty("country_code").GetString() ?? "";
            var admin1 = r.TryGetProperty("admin1", out var a1) ? a1.GetString() : null;
            var lat = r.GetProperty("latitude").GetDouble();
            var lon = r.GetProperty("longitude").GetDouble();
            var tz = r.TryGetProperty("timezone", out var tze) ? tze.GetString() : "auto";

            locations.Add(new WeatherLocation(name, country, admin1, lat, lon, tz ?? "auto"));
        }

        return locations;
    }

    public async Task<WeatherReport> GetForecastAsync(WeatherLocation location, CancellationToken ct = default)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?" +
                  $"latitude={location.Latitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&longitude={location.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                  $"&current=temperature_2m,apparent_temperature,weather_code,precipitation,wind_speed_10m" +
                  $"&hourly=temperature_2m,precipitation_probability,weather_code" +
                  $"&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max" +
                  $"&timezone={Uri.EscapeDataString(location.Timezone)}" +
                  $"&forecast_days=3&format=json";

        var json = await Http.GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);

        CurrentWeatherSnapshot? current = null;
        IReadOnlyList<DailyWeatherForecast> daily = Array.Empty<DailyWeatherForecast>();
        var alerts = Array.Empty<WeatherAlertItem>();

        if (doc.RootElement.TryGetProperty("current", out var cur))
        {
            var temp = cur.GetProperty("temperature_2m").GetDouble();
            var appTemp = cur.TryGetProperty("apparent_temperature", out var at) ? at.GetDouble() : (double?)null;
            var wc = cur.GetProperty("weather_code").GetInt32();
            var precip = cur.TryGetProperty("precipitation", out var p) ? p.GetDouble() : (double?)null;
            var wind = cur.TryGetProperty("wind_speed_10m", out var w) ? w.GetDouble() : (double?)null;
            var updatedAt = DateTimeOffset.UtcNow;

            if (cur.TryGetProperty("time", out var timeEl) && timeEl.GetString() is string ts
                && DateTimeOffset.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                updatedAt = parsed;

            current = new CurrentWeatherSnapshot(
                location, updatedAt, temp, appTemp, wc,
                MapWmoCodeToKey(wc), precip, wind);
        }

        if (doc.RootElement.TryGetProperty("daily", out var dly))
        {
            var dates = ParseDateArray(dly, "time");
            var codes = ParseIntArray(dly, "weather_code");
            var mins = ParseDoubleNullableArray(dly, "temperature_2m_min");
            var maxs = ParseDoubleNullableArray(dly, "temperature_2m_max");
            var precipProbs = ParseIntNullableArray(dly, "precipitation_probability_max");

            var count = Math.Min(dates.Length, Math.Min(codes.Length,
                Math.Min(mins.Length, Math.Min(maxs.Length, precipProbs.Length))));

            var list = new List<DailyWeatherForecast>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(new DailyWeatherForecast(
                    dates[i], codes[i], mins[i], maxs[i], precipProbs[i]));
            }
            daily = list.AsReadOnly();
        }

        return new WeatherReport(current, daily, alerts);
    }

    public static string MapWmoCodeToKey(int wmoCode) => wmoCode switch
    {
        0 => "clear",
        1 => "mainly_clear",
        2 => "partly_cloudy",
        3 => "overcast",
        45 or 48 => "fog",
        51 or 53 or 55 => "drizzle",
        56 or 57 => "freezing_drizzle",
        61 or 63 or 65 => "rain",
        66 or 67 => "freezing_rain",
        71 or 73 or 75 => "snow",
        77 => "snow_grains",
        80 or 81 or 82 => "rain_showers",
        85 or 86 => "snow_showers",
        95 or 96 or 99 => "thunderstorm",
        _ => "unknown"
    };

    public static bool IsSignificantWeather(int wmoCode) =>
        wmoCode is >= 51 and <= 67 or >= 71 and <= 86 or >= 95 and <= 99;

    public async Task<string?> ReverseGeocodeAsync(double lat, double lon, string language)
    {
        try
        {
            var lang = language switch { "ko" => "ko", "zh" => "zh", "ja" => "ja", _ => "en" };
            var url = $"https://nominatim.openstreetmap.org/reverse?" +
                      $"format=jsonv2&lat={lat.ToString(CultureInfo.InvariantCulture)}" +
                      $"&lon={lon.ToString(CultureInfo.InvariantCulture)}" +
                      $"&accept-language={lang}";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(
                $"ClaudeUsageTray/{UpdateService.CurrentVersion.ToString(3)} " +
                "(https://github.com/jeiel85/claude-usage-tray-windows)");

            var json = await Http.SendAsync(req);
            json.EnsureSuccessStatusCode();
            var body = await json.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("display_name", out var name))
                return name.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }

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

    private static int[] ParseIntArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr)) return Array.Empty<int>();
        var list = new List<int>();
        foreach (var item in arr.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Number ? item.GetInt32() : 0);
        return list.ToArray();
    }

    private static double?[] ParseDoubleNullableArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr)) return Array.Empty<double?>();
        var list = new List<double?>();
        foreach (var item in arr.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Null ? null : item.GetDouble());
        return list.ToArray();
    }

    private static int?[] ParseIntNullableArray(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr)) return Array.Empty<int?>();
        var list = new List<int?>();
        foreach (var item in arr.EnumerateArray())
            list.Add(item.ValueKind == JsonValueKind.Null ? null : item.GetInt32());
        return list.ToArray();
    }
}
