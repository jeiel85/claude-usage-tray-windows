using System.Globalization;
using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services.WeatherWarnings;

namespace ClaudeUsageTray.Services;

public class WeatherAlertService
{
    private readonly WeatherService _weather;
    private readonly NotificationService _notifier;
    private readonly Func<NotificationSettings> _getSettings;
    private readonly IReadOnlyList<IWeatherWarningProvider> _warningProviders;

    private static readonly string AlertCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "claude-usage-tray-weather-alerts.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public WeatherAlertService(
        WeatherService weather,
        NotificationService notifier,
        Func<NotificationSettings> getSettings,
        IReadOnlyList<IWeatherWarningProvider> warningProviders)
    {
        _weather = weather;
        _notifier = notifier;
        _getSettings = getSettings;
        _warningProviders = warningProviders;
    }

    public async Task ProcessAlertsAsync(WeatherReport report)
    {
        var settings = _getSettings();
        if (!settings.WeatherEnabled) return;

        var ntfyTopic = settings.NtfySendFromThisPc ? settings.NtfyTopic : "";

        var cache = LoadAlertCache();
        var now = DateTimeOffset.Now;

        if (report.Current != null)
        {
            var loc = report.Current.Location;

            if (settings.WeatherDailyForecastEnabled && report.Daily.Count > 0)
            {
                var today = report.Daily[0];
                var dailyKey = $"daily:{DateOnly.FromDateTime(now.DateTime):yyyyMMdd}:" +
                              $"{loc.Latitude:F2}:{loc.Longitude:F2}";

                if (!cache.SentKeys.Contains(dailyKey) && IsDailyForecastTime(settings))
                {
                    var title = Loc.WeatherForecastTitle;
                    var condLabel = GetConditionLabel(today.WeatherCode);
                    var body = $"{loc.Name}: {condLabel}";
                    if (today.MaxTemperatureC.HasValue && today.MinTemperatureC.HasValue)
                        body += $", {today.MinTemperatureC.Value:F0}/{today.MaxTemperatureC.Value:F0}°C";
                    if (today.PrecipitationProbabilityMax.HasValue)
                        body += $", {Loc.WeatherRainProbability(today.PrecipitationProbabilityMax.Value)}";

                    var ntfyBody = $"{loc.Name}: {condLabel}";
                    if (today.MinTemperatureC.HasValue && today.MaxTemperatureC.HasValue)
                        ntfyBody += $"\n{Loc.WeatherDailyTemp(today.MinTemperatureC.Value, today.MaxTemperatureC.Value)}";
                    ntfyBody += $"\n{Loc.WeatherCurrentTemp(report.Current.TemperatureC)}";
                    if (report.Current.ApparentTemperatureC.HasValue)
                        ntfyBody += $" ({Loc.WeatherFeelsLike(report.Current.ApparentTemperatureC.Value)})";
                    if (today.PrecipitationProbabilityMax.HasValue)
                        ntfyBody += $"\n{Loc.WeatherRainProbability(today.PrecipitationProbabilityMax.Value)}";

                    var clickUrl = BuildWeatherClickUrl(loc);

                    _notifier.ShowWeatherAlert(title, ntfyBody, ntfyTopic,
                        ntfyBody, tags: ["sunny"], clickUrl: clickUrl);
                    cache.SentKeys.Add(dailyKey);
                }
            }

            if (settings.WeatherConditionAlertsEnabled)
            {
                CheckConditionAlerts(report, loc, cache, ntfyTopic, now);
            }

            if (settings.WeatherOfficialAlertsEnabled)
            {
                await CheckOfficialAlerts(loc, cache, ntfyTopic, report);
            }
        }

        PruneCache(cache, now);
        SaveAlertCache(cache);
    }

    private void CheckConditionAlerts(
        WeatherReport report, WeatherLocation loc, AlertCache cache,
        string ntfyTopic, DateTimeOffset now)
    {
        var settings = _getSettings();
        var today = report.Daily.Count > 0 ? report.Daily[0] : null;
        var clickUrl = BuildWeatherClickUrl(loc);

        if (today != null)
        {
            var hourWindow = now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);

            if (today.PrecipitationProbabilityMax >= settings.WeatherRainProbabilityThreshold
                && IsSignificantPrecip(today.WeatherCode))
            {
                var key = $"condition:rain:{hourWindow}:{settings.WeatherRainProbabilityThreshold}:" +
                         $"{loc.Latitude:F2}:{loc.Longitude:F2}";
                if (!cache.SentKeys.Contains(key))
                {
                    var body = $"{loc.Name}: {Loc.WeatherRainWarning(today.PrecipitationProbabilityMax.Value)}";
                    _notifier.ShowWeatherAlert(
                        Loc.WeatherWarningTitle, body, ntfyTopic, body,
                        tags: ["umbrella"], clickUrl: clickUrl);
                    cache.SentKeys.Add(key);
                }
            }

            if (today.MaxTemperatureC >= settings.WeatherHighTemperatureThresholdC)
            {
                var key = $"condition:heat:{hourWindow}:{(int)settings.WeatherHighTemperatureThresholdC}:" +
                         $"{loc.Latitude:F2}:{loc.Longitude:F2}";
                if (!cache.SentKeys.Contains(key))
                {
                    var body = $"{loc.Name}: {Loc.WeatherHeatWarning(today.MaxTemperatureC.Value)}";
                    _notifier.ShowWeatherAlert(
                        Loc.WeatherWarningTitle, body, ntfyTopic, body,
                        tags: ["hot"], clickUrl: clickUrl);
                    cache.SentKeys.Add(key);
                }
            }

            if (today.MinTemperatureC <= settings.WeatherLowTemperatureThresholdC)
            {
                var key = $"condition:cold:{hourWindow}:{(int)settings.WeatherLowTemperatureThresholdC}:" +
                         $"{loc.Latitude:F2}:{loc.Longitude:F2}";
                if (!cache.SentKeys.Contains(key))
                {
                    var body = $"{loc.Name}: {Loc.WeatherColdWarning(today.MinTemperatureC.Value)}";
                    _notifier.ShowWeatherAlert(
                        Loc.WeatherWarningTitle, body, ntfyTopic, body,
                        tags: ["snowflake"], clickUrl: clickUrl);
                    cache.SentKeys.Add(key);
                }
            }
        }

        if (report.Current?.WindSpeedKmh >= settings.WeatherWindSpeedThresholdKmh)
        {
            var hourWindow = now.ToString("yyyyMMddHH", CultureInfo.InvariantCulture);
            var key = $"condition:wind:{hourWindow}:{(int)settings.WeatherWindSpeedThresholdKmh}:" +
                     $"{loc.Latitude:F2}:{loc.Longitude:F2}";
            if (!cache.SentKeys.Contains(key))
            {
                var body = $"{loc.Name}: {Loc.WeatherWindWarning(report.Current.WindSpeedKmh.Value)}";
                _notifier.ShowWeatherAlert(
                    Loc.WeatherWarningTitle, body, ntfyTopic, body,
                    tags: ["wind"], clickUrl: clickUrl);
                cache.SentKeys.Add(key);
            }
        }
    }

    private async Task CheckOfficialAlerts(
        WeatherLocation loc, AlertCache cache, string ntfyTopic, WeatherReport report)
    {
        var clickUrl = BuildWeatherClickUrl(loc);

        foreach (var provider in _warningProviders)
        {
            if (!provider.Supports(loc)) continue;

            var alerts = await provider.GetActiveAlertsAsync(loc);
            foreach (var alert in alerts)
            {
                var key = $"nws:{alert.Id}";
                if (cache.SentKeys.Contains(key)) continue;

                var body = $"{alert.Event} · {alert.Severity}\n{alert.Headline}";
                _notifier.ShowWeatherAlert(
                    Loc.WeatherWarningTitle, body, ntfyTopic, body,
                    tags: ["warning"], clickUrl: clickUrl);
                cache.SentKeys.Add(key);
            }

            break;
        }
    }

    private static bool IsDailyForecastTime(NotificationSettings settings)
    {
        var timeParts = settings.WeatherDailyForecastTime.Split(':');
        if (timeParts.Length != 2
            || !int.TryParse(timeParts[0], out var h)
            || !int.TryParse(timeParts[1], out var m))
            return false;

        var now = DateTime.Now;
        var target = new DateTime(now.Year, now.Month, now.Day, h, m, 0);
        return now >= target && now < target.AddMinutes(30);
    }

    private static string BuildWeatherClickUrl(WeatherLocation loc)
    {
        return BuildWeatherClickUrlPublic(loc);
    }

    public static string BuildWeatherClickUrlPublic(WeatherLocation loc)
    {
        var lat = loc.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = loc.Longitude.ToString(CultureInfo.InvariantCulture);
        return $"https://www.meteoblue.com/en/weather/week/{lat}N{lon}E12";
    }

    private static bool IsSignificantPrecip(int wmoCode) =>
        wmoCode is >= 51 and <= 67 or >= 71 and <= 86;

    private static string GetConditionLabel(int wmoCode)
    {
        var key = WeatherService.MapWmoCodeToKey(wmoCode);
        return key switch
        {
            "clear" => Loc.WeatherClear,
            "mainly_clear" => Loc.WeatherMainlyClear,
            "partly_cloudy" => Loc.WeatherPartlyCloudy,
            "overcast" => Loc.WeatherOvercast,
            "fog" => Loc.WeatherFog,
            "drizzle" or "freezing_drizzle" => Loc.WeatherDrizzle,
            "rain" or "freezing_rain" or "rain_showers" => Loc.WeatherRain,
            "snow" or "snow_grains" or "snow_showers" => Loc.WeatherSnow,
            "thunderstorm" => Loc.WeatherThunderstorm,
            _ => Loc.WeatherUnknown
        };
    }

    private static AlertCache LoadAlertCache()
    {
        try
        {
            if (File.Exists(AlertCachePath))
            {
                var json = File.ReadAllText(AlertCachePath);
                return JsonSerializer.Deserialize<AlertCache>(json, JsonOpts) ?? new();
            }
        }
        catch { }
        return new AlertCache();
    }

    private static void SaveAlertCache(AlertCache cache)
    {
        try
        {
            var dir = Path.GetDirectoryName(AlertCachePath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(AlertCachePath, JsonSerializer.Serialize(cache, JsonOpts));
        }
        catch { }
    }

    private static void PruneCache(AlertCache cache, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-7);
        cache.SentKeys.RemoveWhere(key =>
        {
            if (key.StartsWith("nws:")) return true;
            return false;
        });

#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[WeatherAlertCache] {cache.SentKeys.Count} keys after prune");
#endif
    }

    private sealed class AlertCache
    {
        public HashSet<string> SentKeys { get; set; } = [];
        public DateTimeOffset LastPrunedAt { get; set; } = DateTimeOffset.MinValue;
    }
}
