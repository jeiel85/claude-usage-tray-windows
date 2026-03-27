using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Timer = System.Timers.Timer;

namespace ClaudeUsageTray.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly UsageApiService _api;
    private readonly SessionMonitor _session;
    private readonly NotificationService _notifier;
    private readonly SettingsService _settingsService;
    private readonly Timer _timer;

    // Tracks previous 5h usage to detect threshold crossings
    private double _prevShortPercent = -1;
    private bool _prevHadRateLimit = false;

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _lastUpdatedLabel = "";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";

    // 5h window
    [ObservableProperty] private double _shortUsagePercent = 0;
    [ObservableProperty] private string _shortResetLabel = "";

    // 7d window
    [ObservableProperty] private double _longUsagePercent = 0;
    [ObservableProperty] private string _longResetLabel = "";

    // Per-model usage (7d)
    [ObservableProperty] private double _opusPercent = 0;
    [ObservableProperty] private double _sonnetPercent = 0;
    [ObservableProperty] private long _opusTokens = 0;
    [ObservableProperty] private long _sonnetTokens = 0;

    // Today's local session stats
    [ObservableProperty] private long _todayInputTokens = 0;
    [ObservableProperty] private long _todayOutputTokens = 0;
    [ObservableProperty] private long _todayCacheRead = 0;
    [ObservableProperty] private long _todayCacheWrite = 0;
    [ObservableProperty] private string _sessionsLabel = "";
    [ObservableProperty] private bool _hasRateLimitHit = false;
    [ObservableProperty] private string _rateLimitInfo = "";

    // Notification settings
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _notifyRateLimit;
    [ObservableProperty] private bool _threshold50;
    [ObservableProperty] private bool _threshold75;
    [ObservableProperty] private bool _threshold90;
    [ObservableProperty] private bool _threshold100;
    [ObservableProperty] private string _ntfyTopic = "";

    public string? RawApiResponse { get; private set; }

    // Localized static labels
    public string LblAppTitle        => Loc.AppTitle;
    public string LblApiQuota        => Loc.ApiQuota;
    public string LblTodayTokens     => Loc.TodayTokens;
    public string LblFiveHour        => Loc.FiveHourWindow;
    public string LblSevenDay        => Loc.SevenDayWindow;
    public string LblInput           => Loc.Input;
    public string LblOutput          => Loc.Output;
    public string LblCacheRead       => Loc.CacheRead;
    public string LblCacheWrite      => Loc.CacheWrite;
    public string LblTokens          => Loc.Tokens;
    public string LblRefresh         => Loc.Refresh;
    public string LblQuit            => Loc.Quit;
    public string LblRefreshing      => Loc.Refreshing;
    public string LblNotifications   => Loc.Notifications;
    public string LblNotiEnabled     => Loc.NotificationsEnabled;
    public string LblNotiRateLimit   => Loc.NotifyRateLimit;
    public string LblThresholds      => Loc.ThresholdsLabel;
    public string LblNtfyTopic       => Loc.NtfyTopic;
    public string LblNtfyPlaceholder => Loc.NtfyPlaceholder;

    public MainViewModel(UsageApiService api, SessionMonitor session,
                         NotificationService notifier, SettingsService settingsService)
    {
        _api = api;
        _session = session;
        _notifier = notifier;
        _settingsService = settingsService;

        LoadSettings();

        _timer = new Timer(30_000);
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = true;
    }

    private void LoadSettings()
    {
        var s = _settingsService.Load();
        NotificationsEnabled = s.Enabled;
        NotifyRateLimit = s.NotifyOnRateLimit;
        Threshold50  = s.Thresholds.Contains(50);
        Threshold75  = s.Thresholds.Contains(75);
        Threshold90  = s.Thresholds.Contains(90);
        Threshold100 = s.Thresholds.Contains(100);
        NtfyTopic    = s.NtfyTopic;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        var thresholds = new List<int>();
        if (Threshold50)  thresholds.Add(50);
        if (Threshold75)  thresholds.Add(75);
        if (Threshold90)  thresholds.Add(90);
        if (Threshold100) thresholds.Add(100);

        _settingsService.Save(new NotificationSettings
        {
            Enabled = NotificationsEnabled,
            NotifyOnRateLimit = NotifyRateLimit,
            Thresholds = thresholds,
            NtfyTopic = NtfyTopic.Trim()
        });
    }

    public async Task StartAsync()
    {
        await RefreshAsync();
        _timer.Start();
    }

    public async Task RefreshAsync()
    {
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);

        try
        {
            var usage = await _api.FetchUsageAsync();
            RawApiResponse = _api.LastRawResponse;
            var sessionStats = _session.ScanTodayUsage();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TodayInputTokens  = sessionStats.TotalInputTokens;
                TodayOutputTokens = sessionStats.TotalOutputTokens;
                TodayCacheRead    = sessionStats.TotalCacheReadTokens;
                TodayCacheWrite   = sessionStats.TotalCacheWriteTokens;
                SessionsLabel     = Loc.Sessions(sessionStats.SessionCount);
                HasRateLimitHit   = sessionStats.HasRateLimitHit;
                RateLimitInfo     = sessionStats.RateLimitResetTime ?? "";

                // Rate limit notification
                if (NotificationsEnabled && NotifyRateLimit &&
                    sessionStats.HasRateLimitHit && !_prevHadRateLimit)
                {
                    _notifier.ShowRateLimitAlert(NtfyTopic);
                }
                _prevHadRateLimit = sessionStats.HasRateLimitHit;

                if (usage?.FiveHour != null || usage?.SevenDay != null)
                {
                    HasError = false;

                    if (usage!.FiveHour != null)
                    {
                        var newPercent = usage.FiveHour.UsagePercent;
                        ShortResetLabel = FormatResetLabel(usage.FiveHour.ResetsAtParsed);

                        // Check threshold crossings (skip on first load)
                        if (NotificationsEnabled && _prevShortPercent >= 0)
                        {
                            CheckThresholds(newPercent, ShortResetLabel, NtfyTopic);
                        }

                        ShortUsagePercent = newPercent;
                        _prevShortPercent = newPercent;
                    }

                    if (usage.SevenDay != null)
                    {
                        LongUsagePercent = usage.SevenDay.UsagePercent;
                        LongResetLabel   = FormatResetLabel(usage.SevenDay.ResetsAtParsed);
                    }

                    if (usage.SevenDayOpus != null)
                    {
                        OpusPercent = usage.SevenDayOpus.UsagePercent;
                        OpusTokens  = (long)usage.SevenDayOpus.Utilization;
                    }

                    if (usage.SevenDaySonnet != null)
                    {
                        SonnetPercent = usage.SevenDaySonnet.UsagePercent;
                        SonnetTokens  = (long)usage.SevenDaySonnet.Utilization;
                    }

                    StatusText = $"{ShortUsagePercent:P0} used";
                }
                else if (_api.LastError != null)
                {
                    HasError = true;
                    ErrorMessage = ParseFriendlyError(_api.LastError);
                    StatusText = "API Error";
                }
                else
                {
                    StatusText = "No data";
                }

                LastUpdatedLabel = Loc.UpdatedAt(DateTime.Now.ToString("HH:mm:ss"));
                IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = ex.Message;
                StatusText = "Error";
                IsLoading = false;
            });
        }
    }

    private void CheckThresholds(double newPercent, string resetLabel, string ntfyTopic)
    {
        var settings = _settingsService.Load();
        foreach (var t in settings.Thresholds.OrderBy(x => x))
        {
            double tf = t / 100.0;
            if (_prevShortPercent < tf && newPercent >= tf)
            {
                _notifier.ShowUsageAlert(t, Loc.FiveHourWindow, resetLabel, ntfyTopic);
            }
        }
    }

    private static string FormatResetLabel(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return "";
        var diff = resetAt.Value - DateTimeOffset.Now;
        if (diff.TotalSeconds <= 0) return "";
        string time;
        if (diff.TotalHours < 1) time = $"{(int)diff.TotalMinutes}m";
        else if (diff.TotalDays < 1) time = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        else time = $"{(int)diff.TotalDays}d {diff.Hours}h";
        return Loc.ResetsIn(time);
    }

    private static string ParseFriendlyError(string raw)
    {
        if (raw.Contains("429") || raw.Contains("rate_limit"))
            return Loc.RateLimited;
        try
        {
            var start = raw.IndexOf('{');
            if (start >= 0)
            {
                var json = raw[start..];
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("error", out var err) &&
                    err.TryGetProperty("message", out var msg))
                {
                    var msgText = msg.GetString() ?? raw;
                    return Loc.ApiError(msgText.Length > 80 ? msgText[..80] + "…" : msgText);
                }
            }
        }
        catch { }
        return Loc.ApiError(raw.Length > 80 ? raw[..80] + "…" : raw);
    }

    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
