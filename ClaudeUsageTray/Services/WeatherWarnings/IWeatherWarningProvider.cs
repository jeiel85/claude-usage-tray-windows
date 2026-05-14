using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.WeatherWarnings;

public interface IWeatherWarningProvider
{
    bool Supports(WeatherLocation location);
    Task<IReadOnlyList<WeatherAlertItem>> GetActiveAlertsAsync(WeatherLocation location, CancellationToken ct = default);
}
