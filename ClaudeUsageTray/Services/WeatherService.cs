using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services.Forecasts;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 위치 검색/역지오코딩과 예보 provider 오케스트레이션을 담당한다.
/// 실제 예보 조회는 <see cref="IForecastProvider"/> 구현체가 수행한다.
/// </summary>
public class WeatherService
{
    /// <summary>예보 소스를 고정하지 않고 provider 순서대로 시도한다는 뜻의 설정값.</summary>
    public const string AutoSource = "auto";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.WeatherTimeoutSeconds)
    };

    private readonly IReadOnlyList<IForecastProvider> _forecastProviders;

    public WeatherService()
        : this([new OpenMeteoForecastProvider(), new MetNorwayForecastProvider()])
    {
    }

    public WeatherService(IReadOnlyList<IForecastProvider> forecastProviders)
    {
        _forecastProviders = forecastProviders;
    }

    /// <summary>설정 UI 에 노출할 예보 소스 식별자 목록.</summary>
    public static readonly IReadOnlyList<string> SelectableSources =
    [
        AutoSource,
        OpenMeteoForecastProvider.ProviderId,
        MetNorwayForecastProvider.ProviderId
    ];

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

    public Task<WeatherReport> GetForecastAsync(WeatherLocation location, CancellationToken ct = default) =>
        GetForecastAsync(location, AutoSource, null, ct);

    /// <summary>
    /// 예보를 조회한다. 선택한 소스를 먼저 시도하고, 그 소스가 데이터를 주지 못하면
    /// 나머지 provider 로 폴백한다. 어느 소스가 실제로 응답했는지는
    /// <see cref="WeatherReport.SourceId"/> 에 담긴다.
    /// </summary>
    /// <param name="sourceId">
    /// <see cref="AutoSource"/> 이거나 provider 의 Id. 알 수 없는 값이면 auto 로 취급한다.
    /// </param>
    /// <param name="modelId">provider 별 예보 모델. 지원하지 않는 provider 는 무시한다.</param>
    /// <exception cref="InvalidOperationException">
    /// 모든 provider 가 데이터를 주지 못했고 예외도 발생하지 않은 경우.
    /// </exception>
    public async Task<WeatherReport> GetForecastAsync(
        WeatherLocation location, string sourceId, string? modelId, CancellationToken ct = default)
    {
        var ordered = OrderProviders(sourceId);
        Exception? lastError = null;

        foreach (var provider in ordered)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // 모델은 사용자가 의도한 1순위 소스에만 적용한다. 소스가 "자동"이면 목록의
                // 첫 provider 가 1순위다 — sourceId 와 provider.Id 를 비교하면 자동일 때
                // 어느 provider 와도 일치하지 않아 모델 선택이 통째로 무시된다.
                // 폴백으로 넘어간 provider 에는 다른 소스의 모델 이름을 넘기지 않는다.
                var effectiveModel = ReferenceEquals(provider, ordered[0]) ? modelId : null;

                var report = await provider.GetForecastAsync(location, effectiveModel, ct);
                if (report?.Current != null)
                    return report;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (lastError != null)
            throw lastError;

        throw new InvalidOperationException(
            $"No forecast provider returned data for {location.Name} ({location.Latitude}, {location.Longitude}).");
    }

    private IReadOnlyList<IForecastProvider> OrderProviders(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId == AutoSource)
            return _forecastProviders;

        var preferred = _forecastProviders.FirstOrDefault(p => p.Id == sourceId);
        if (preferred == null)
            return _forecastProviders;

        return [preferred, .. _forecastProviders.Where(p => p != preferred)];
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
            // addressdetails=1: structured 'address' object so we can pick city-level granularity
            // and avoid leaking building/amenity/road into the display name.
            var url = $"https://nominatim.openstreetmap.org/reverse?" +
                      $"format=jsonv2&addressdetails=1" +
                      $"&lat={lat.ToString(CultureInfo.InvariantCulture)}" +
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
            if (doc.RootElement.TryGetProperty("address", out var addr))
            {
                // Prefer the most administratively meaningful name available, in order
                // of granularity. Falls through to display_name only as last resort.
                foreach (var key in new[] { "city", "town", "village", "municipality",
                                             "county", "city_district", "suburb",
                                             "state_district", "state" })
                {
                    if (addr.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } s)
                        return s;
                }
            }

            if (doc.RootElement.TryGetProperty("display_name", out var name))
                return name.GetString();

            return null;
        }
        catch
        {
            return null;
        }
    }
}
