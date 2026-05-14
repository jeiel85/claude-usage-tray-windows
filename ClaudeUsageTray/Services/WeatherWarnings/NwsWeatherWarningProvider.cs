using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.WeatherWarnings;

public class NwsWeatherWarningProvider : IWeatherWarningProvider
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.WeatherTimeoutSeconds)
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NwsWeatherWarningProvider()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd(
            $"ClaudeUsageTray/{UpdateService.CurrentVersion.ToString(3)} " +
            "(https://github.com/jeiel85/claude-usage-tray-windows)");
    }

    public bool Supports(WeatherLocation location) =>
        string.Equals(location.CountryCode, "US", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WeatherAlertItem>> GetActiveAlertsAsync(
        WeatherLocation location, CancellationToken ct = default)
    {
        try
        {
            var url = $"https://api.weather.gov/alerts/active?" +
                      $"point={location.Latitude.ToString(CultureInfo.InvariantCulture)}," +
                      $"{location.Longitude.ToString(CultureInfo.InvariantCulture)}";

            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("features", out var features))
                return Array.Empty<WeatherAlertItem>();

            var alerts = new List<WeatherAlertItem>();
            foreach (var feature in features.EnumerateArray())
            {
                var props = feature.GetProperty("properties");
                var id = props.TryGetProperty("id", out var idEl) ? idEl.GetString() : "";
                var evt = props.TryGetProperty("event", out var evtEl) ? evtEl.GetString() : "";
                var severity = props.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() : "";
                var headline = props.TryGetProperty("headline", out var hlEl) ? hlEl.GetString() : "";
                var effective = ParseNwsTime(props, "effective");
                var expires = ParseNwsTime(props, "expires");

                alerts.Add(new WeatherAlertItem(
                    "NWS", id ?? "", evt ?? "", severity ?? "",
                    headline ?? "", effective, expires));
            }

            return alerts;
        }
        catch
        {
            return Array.Empty<WeatherAlertItem>();
        }
    }

    private static DateTimeOffset? ParseNwsTime(JsonElement props, string key)
    {
        if (props.TryGetProperty(key, out var el)
            && el.GetString() is string s
            && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return dto;
        return null;
    }
}
