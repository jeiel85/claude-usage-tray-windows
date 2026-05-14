namespace ClaudeUsageTray.Models;

public sealed record WeatherLocation(
    string Name,
    string CountryCode,
    string? Admin1,
    double Latitude,
    double Longitude,
    string Timezone);

public sealed record CurrentWeatherSnapshot(
    WeatherLocation Location,
    DateTimeOffset UpdatedAt,
    double TemperatureC,
    double? ApparentTemperatureC,
    int WeatherCode,
    string ConditionKey,
    double? PrecipitationMm,
    double? WindSpeedKmh);

public sealed record DailyWeatherForecast(
    DateOnly Date,
    int WeatherCode,
    double? MinTemperatureC,
    double? MaxTemperatureC,
    int? PrecipitationProbabilityMax);

public sealed record WeatherAlertItem(
    string Source,
    string Id,
    string Event,
    string Severity,
    string Headline,
    DateTimeOffset? Effective,
    DateTimeOffset? Expires);

public sealed record WeatherReport(
    CurrentWeatherSnapshot? Current,
    IReadOnlyList<DailyWeatherForecast> Daily,
    IReadOnlyList<WeatherAlertItem> Alerts);
