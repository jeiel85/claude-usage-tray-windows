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
        WriteIndented = true,
        // 마이그레이션이 끝난 구형식 SentKeys 를 파일에 다시 쓰지 않기 위해 필요하다.
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>발송 기록 보관 기간. 이보다 오래된 키는 정리한다.</summary>
    private const int CacheRetentionDays = 7;

    /// <summary>현재 관측값으로 판정하는 조건 알림의 재발송 간격(시간).</summary>
    private const int ConditionCooldownHours = 6;

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

        var now = DateTimeOffset.Now;
        var cache = LoadAlertCache(now);

        if (report.Current != null)
        {
            var loc = report.Current.Location;

            if (settings.WeatherDailyForecastEnabled && report.Daily.Count > 0)
            {
                var today = report.Daily[0];
                var dailyKey = $"daily:{DateOnly.FromDateTime(now.DateTime):yyyyMMdd}:" +
                              $"{loc.Latitude:F2}:{loc.Longitude:F2}";

                if (!cache.Contains(dailyKey) && IsDailyForecastTime(settings))
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
                    cache.Mark(dailyKey, now);
                }
            }

            if (settings.WeatherConditionAlertsEnabled)
            {
                CheckConditionAlerts(report, loc, cache, ntfyTopic, now);
            }

            if (settings.WeatherOfficialAlertsEnabled)
            {
                await CheckOfficialAlerts(loc, cache, ntfyTopic, now);
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
            // 비/폭염/한파는 "오늘 예보"(report.Daily[0]) 하나를 보고 판정하므로 하루 안에서는
            // 같은 내용이다. 하루 한 번만 보낸다.
            var dayWindow = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            if (today.PrecipitationProbabilityMax >= settings.WeatherRainProbabilityThreshold
                && IsSignificantPrecip(today.WeatherCode))
            {
                var key = $"condition:rain:{dayWindow}:{settings.WeatherRainProbabilityThreshold}:" +
                         $"{loc.Latitude:F2}:{loc.Longitude:F2}";
                if (!cache.Contains(key))
                {
                    var body = $"{loc.Name}: {Loc.WeatherRainWarning(today.PrecipitationProbabilityMax.Value)}";
                    _notifier.ShowWeatherAlert(
                        Loc.WeatherWarningTitle, body, ntfyTopic, body,
                        tags: ["umbrella"], clickUrl: clickUrl);
                    cache.Mark(key, now);
                }
            }

            if (today.MaxTemperatureC >= settings.WeatherHighTemperatureThresholdC)
            {
                var key = $"condition:heat:{dayWindow}:{(int)settings.WeatherHighTemperatureThresholdC}:" +
                         $"{loc.Latitude:F2}:{loc.Longitude:F2}";
                if (!cache.Contains(key))
                {
                    var body = $"{loc.Name}: {Loc.WeatherHeatWarning(today.MaxTemperatureC.Value)}";
                    _notifier.ShowWeatherAlert(
                        Loc.WeatherWarningTitle, body, ntfyTopic, body,
                        tags: ["hot"], clickUrl: clickUrl);
                    cache.Mark(key, now);
                }
            }

            if (today.MinTemperatureC <= settings.WeatherLowTemperatureThresholdC)
            {
                var key = $"condition:cold:{dayWindow}:{(int)settings.WeatherLowTemperatureThresholdC}:" +
                         $"{loc.Latitude:F2}:{loc.Longitude:F2}";
                if (!cache.Contains(key))
                {
                    var body = $"{loc.Name}: {Loc.WeatherColdWarning(today.MinTemperatureC.Value)}";
                    _notifier.ShowWeatherAlert(
                        Loc.WeatherWarningTitle, body, ntfyTopic, body,
                        tags: ["snowflake"], clickUrl: clickUrl);
                    cache.Mark(key, now);
                }
            }
        }

        if (report.Current?.WindSpeedKmh >= settings.WeatherWindSpeedThresholdKmh)
        {
            // 강풍은 예보가 아니라 현재 관측 풍속으로 판정하므로 하루 안에서도 값이 바뀐다.
            // 설계 문서의 조건 알림 쿨다운(6시간)을 그대로 적용한다.
            var windWindow = $"{now:yyyyMMdd}-{now.Hour / ConditionCooldownHours}";
            var key = $"condition:wind:{windWindow}:{(int)settings.WeatherWindSpeedThresholdKmh}:" +
                     $"{loc.Latitude:F2}:{loc.Longitude:F2}";
            if (!cache.Contains(key))
            {
                var body = $"{loc.Name}: {Loc.WeatherWindWarning(report.Current.WindSpeedKmh.Value)}";
                _notifier.ShowWeatherAlert(
                    Loc.WeatherWarningTitle, body, ntfyTopic, body,
                    tags: ["wind"], clickUrl: clickUrl);
                cache.Mark(key, now);
            }
        }
    }

    private async Task CheckOfficialAlerts(
        WeatherLocation loc, AlertCache cache, string ntfyTopic, DateTimeOffset now)
    {
        var clickUrl = BuildWeatherClickUrl(loc);

        foreach (var provider in _warningProviders)
        {
            if (!provider.Supports(loc)) continue;

            var alerts = await provider.GetActiveAlertsAsync(loc);
            foreach (var alert in alerts)
            {
                // provider 마다 Id 체계가 달라 소스명을 접두사로 붙여 충돌을 막는다.
                var key = $"official:{alert.Source}:{alert.Id}";
                if (cache.Contains(key)) continue;

                var body = $"{alert.Event} · {alert.Severity}\n{alert.Headline}";
                _notifier.ShowWeatherAlert(
                    Loc.WeatherWarningTitle, body, ntfyTopic, body,
                    tags: ["warning"], clickUrl: clickUrl);
                cache.Mark(key, now);
            }

            // 한 좌표가 두 나라의 공식 특보 구역에 동시에 속하지는 않으므로
            // 지원하는 provider 를 하나 찾으면 거기서 멈춘다.
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

    private static AlertCache LoadAlertCache(DateTimeOffset now)
    {
        AlertCache cache;
        try
        {
            cache = File.Exists(AlertCachePath)
                ? JsonSerializer.Deserialize<AlertCache>(File.ReadAllText(AlertCachePath), JsonOpts) ?? new()
                : new AlertCache();
        }
        catch
        {
            cache = new AlertCache();
        }

        cache.MigrateLegacyKeys(now);
        return cache;
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

    internal static void PruneCache(AlertCache cache, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-CacheRetentionDays);

        foreach (var key in cache.Entries.Where(e => e.Value < cutoff).Select(e => e.Key).ToList())
            cache.Entries.Remove(key);

        cache.LastPrunedAt = now;

#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[WeatherAlertCache] {cache.Entries.Count} keys after prune");
#endif
    }

    internal sealed class AlertCache
    {
        /// <summary>
        /// v1.36.x 이전 형식. 발송 시각이 없어 정리할 수 없었다. 로드 시 <see cref="Entries"/>
        /// 로 옮기고 다시 저장하지 않는다.
        /// </summary>
        public HashSet<string>? SentKeys { get; set; }

        /// <summary>발송한 알림 키와 발송 시각.</summary>
        public Dictionary<string, DateTimeOffset> Entries { get; set; } = [];

        public DateTimeOffset LastPrunedAt { get; set; } = DateTimeOffset.MinValue;

        public bool Contains(string key) => Entries.ContainsKey(key);

        public void Mark(string key, DateTimeOffset sentAt) => Entries[key] = sentAt;

        /// <summary>
        /// 구형식 키를 흡수한다. 발송 시각을 알 수 없으므로 보존 기간이 막 지나기 직전 시각을
        /// 부여해, 다음 정리 때 자연스럽게 빠지면서도 당장은 재발송을 막게 한다.
        /// </summary>
        public void MigrateLegacyKeys(DateTimeOffset now)
        {
            if (SentKeys == null) return;

            var assumedSentAt = now.AddDays(-CacheRetentionDays).AddHours(1);
            foreach (var key in SentKeys)
                Entries.TryAdd(key, assumedSentAt);

            SentKeys = null;
        }
    }
}
