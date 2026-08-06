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

/// <param name="SourceId">
/// 이 리포트를 실제로 만들어 낸 예보 provider 의 Id. 사용자가 고른 소스가 데이터를
/// 주지 못해 폴백된 경우를 구분하려면 이 값을 봐야 한다. 빈 문자열은 출처 미상.
/// </param>
public sealed record WeatherReport(
    CurrentWeatherSnapshot? Current,
    IReadOnlyList<DailyWeatherForecast> Daily,
    IReadOnlyList<WeatherAlertItem> Alerts,
    string SourceId = "");
