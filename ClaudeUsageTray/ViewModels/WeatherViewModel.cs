using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class WeatherViewModel : ObservableObject
{
    private readonly WeatherService _weather;
    private readonly WeatherAlertService _weatherAlert;
    private WeatherReport? _lastReport;
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private bool _showInTrayTooltip = true;
    [ObservableProperty] private string _locationMode = "manual";
    [ObservableProperty] private string _locationName = "";
    [ObservableProperty] private string _countryCode = "";
    [ObservableProperty] private double? _latitude;
    [ObservableProperty] private double? _longitude;
    [ObservableProperty] private string _timezone = "auto";
    [ObservableProperty] private int _refreshIntervalMinutes = 30;
    [ObservableProperty] private bool _dailyForecastEnabled = true;
    [ObservableProperty] private string _dailyForecastTime = "07:30";
    [ObservableProperty] private bool _conditionAlertsEnabled = true;
    [ObservableProperty] private int _rainProbabilityThreshold = 70;
    [ObservableProperty] private double _highTemperatureThresholdC = 33;
    [ObservableProperty] private double _lowTemperatureThresholdC = -10;
    [ObservableProperty] private double _windSpeedThresholdKmh = 50;
    [ObservableProperty] private bool _officialAlertsEnabled = true;
    [ObservableProperty] private string _statusLabel = "";
    [ObservableProperty] private string _tooltipLabel = "";
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _temperatureLabel = "";
    [ObservableProperty] private string _conditionLabel = "";
    [ObservableProperty] private string _icon = "•";

    public bool HasLocation => Enabled && Latitude.HasValue && Longitude.HasValue && !string.IsNullOrEmpty(LocationName);
    public bool HasCurrent => HasLocation && !string.IsNullOrEmpty(TemperatureLabel);
    public string PopupLabel => HasLocation && !string.IsNullOrEmpty(TooltipLabel) ? $"📍 {TooltipLabel}" : "";

    public string ShortLocation
    {
        get
        {
            if (string.IsNullOrEmpty(LocationName)) return "";
            var parts = LocationName.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            if (parts.Length <= 1) return parts.Length == 1 ? parts[0] : LocationName;

            string[] citySuffixes = ["시", "구", "군", "City", "Town", "市", "区"];
            foreach (var p in parts)
            {
                foreach (var suf in citySuffixes)
                {
                    if (p.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }

            foreach (var p in parts)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(p, @"^[\d\s\-]+$"))
                    return p;
            }

            return parts[0];
        }
    }

    public WeatherViewModel(WeatherService weather, WeatherAlertService weatherAlert)
    {
        _weather = weather;
        _weatherAlert = weatherAlert;
    }

    public async Task RefreshAsync()
    {
        if (!Latitude.HasValue || !Longitude.HasValue) return;

        var interval = TimeSpan.FromMinutes(RefreshIntervalMinutes);
        if (DateTimeOffset.Now - _lastRefresh < interval) return;

        try
        {
            var location = new WeatherLocation(LocationName, CountryCode, null, Latitude.Value, Longitude.Value, Timezone);
            var report = await _weather.GetForecastAsync(location);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasError = false;
                ErrorMessage = "";
                _lastReport = report;

                if (report.Current != null)
                {
                    var condLabel = GetConditionLabel(report.Current.ConditionKey);
                    var tempLabel = $"{report.Current.TemperatureC:F0}°C";
                    TemperatureLabel = tempLabel;
                    ConditionLabel = condLabel;
                    Icon = GetIcon(report.Current.ConditionKey);
                    StatusLabel = $"{tempLabel} {condLabel}";
                    TooltipLabel = Loc.WeatherTooltipFormat(ShortLocation, report.Current.TemperatureC, condLabel);
                }
                else
                {
                    TemperatureLabel = "";
                    ConditionLabel = Loc.WeatherCurrentUnavailable;
                    Icon = "•";
                    TooltipLabel = $"{ShortLocation} {Loc.WeatherCurrentUnavailable}";
                    StatusLabel = Loc.WeatherCurrentUnavailable;
                }

                HasError = report.Current == null;
                if (HasError && string.IsNullOrEmpty(ErrorMessage))
                    ErrorMessage = Loc.WeatherCurrentUnavailable;
            });

            _lastRefresh = DateTimeOffset.Now;

            if (report.Current != null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _weatherAlert.ProcessAlertsAsync(report);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Weather alert error: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = ex.Message;
                TooltipLabel = Loc.WeatherCurrentUnavailable;
                TemperatureLabel = "";
                ConditionLabel = Loc.WeatherCurrentUnavailable;
                Icon = "•";
            });
        }
    }

    internal static string GetIcon(string conditionKey) => conditionKey switch
    {
        "clear" or "mainly_clear" => "☀",
        "partly_cloudy" => "⛅",
        "overcast" or "fog" => "☁",
        "drizzle" or "freezing_drizzle" => "☂",
        "rain" or "freezing_rain" or "rain_showers" => "☔",
        "snow" or "snow_grains" or "snow_showers" => "❄",
        "thunderstorm" => "⚡",
        _ => "•"
    };

    internal static string GetConditionLabel(string conditionKey) => conditionKey switch
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

    partial void OnLocationNameChanged(string value) => OnPropertyChanged(nameof(ShortLocation));
    partial void OnTooltipLabelChanged(string value) => OnPropertyChanged(nameof(PopupLabel));
    partial void OnTemperatureLabelChanged(string value) => OnPropertyChanged(nameof(HasCurrent));
}
