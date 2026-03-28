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
    private readonly UpdateService _updater;
    private readonly HistoryService _history;
    private readonly Timer _timer;
    private readonly Timer _countdownTimer;
    private int _secondsUntilRefresh = 0;

    // Tracks previous 5h usage to detect threshold crossings
    private double _prevShortPercent = -1;
    private bool _prevHadRateLimit = false;

    // Last known good API data (kept when rate-limited so UI doesn't reset to 0)
    private double _lastKnownShortPercent = 0;
    private double _lastKnownLongPercent = 0;
    private string _lastKnownShortReset = "";
    private string _lastKnownLongReset = "";

    // Rate limit backoff — skip API calls until this time
    private DateTimeOffset _apiRetryAfter = DateTimeOffset.MinValue;

    [ObservableProperty] private string _statusText = "Loading...";
    [ObservableProperty] private string _nextRefreshLabel = "";
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
    [ObservableProperty] private bool _startWithWindows;

    // History
    [ObservableProperty] private IReadOnlyList<DailyStats> _historyData = [];

    // Update banner
    [ObservableProperty] private bool _updateAvailable = false;
    [ObservableProperty] private string _updateLabel = "";
    private string _updateDownloadUrl = "";
    public string CurrentVersionLabel => $"v{UpdateService.CurrentVersion.ToString(3)}";

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
    public string LblHistory         => Loc.HistoryTitle;
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
                         NotificationService notifier, SettingsService settingsService,
                         UpdateService updater, HistoryService history)
    {
        _api = api;
        _session = session;
        _notifier = notifier;
        _settingsService = settingsService;
        _updater = updater;
        _history = history;

        LoadSettings();

        _timer = new Timer(120_000); // 2 minutes — API has rate limits
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = true;

        _countdownTimer = new Timer(1_000);
        _countdownTimer.Elapsed += (_, _) =>
        {
            if (_secondsUntilRefresh > 0)
                _secondsUntilRefresh--;
            var s = _secondsUntilRefresh;
            var label = s >= 60 ? $"{s / 60}:{s % 60:D2}" : $"{s}s";
            System.Windows.Application.Current.Dispatcher.Invoke(() => NextRefreshLabel = label);
        };
        _countdownTimer.AutoReset = true;
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
        NtfyTopic         = s.NtfyTopic;
        StartWithWindows  = s.StartWithWindows;
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
            NtfyTopic = NtfyTopic.Trim(),
            StartWithWindows = StartWithWindows
        });
    }

    public async Task StartAsync()
    {
        await RefreshAsync();
        _timer.Start();
        _countdownTimer.Start();
        _ = CheckForUpdateAsync();
    }

    private async Task CheckForUpdateAsync()
    {
        var result = await _updater.CheckForUpdateAsync();
        if (result is null) return;

        var (version, url, releaseNotes) = result.Value;
        var versionStr = version.ToString(3);

        // Check if user skipped this version
        var settings = _settingsService.Load();
        if (settings.SkippedVersion == versionStr) return;

        _updateDownloadUrl = url;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateLabel = Loc.UpdateAvailable($"v{versionStr}");
            UpdateAvailable = true;

            var dialog = new Views.UpdateDialog(
                $"v{versionStr}",
                releaseNotes,
                onUpdate: async () => await ApplyUpdateAsync(),
                onSkip: () => SkipVersion(versionStr));
            dialog.Show();
        });
    }

    public void SkipVersion(string version)
    {
        var settings = _settingsService.Load();
        settings.SkippedVersion = version;
        _settingsService.Save(settings);
        UpdateAvailable = false;
    }

    [RelayCommand]
    public void ExportCsv()
    {
        var filePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            $"claude-usage-{DateTime.Now:yyyyMMdd}.csv");
        _history.ExportCsv(filePath);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    public void SendTestNotification()
    {
        _notifier.ShowTestAlert(NtfyTopic);
    }

    [RelayCommand]
    public async Task ApplyUpdateAsync()
    {
        if (string.IsNullOrEmpty(_updateDownloadUrl)) return;
        await _updater.ApplyUpdateAsync(_updateDownloadUrl);
    }

    public async Task RefreshAsync()
    {
        _secondsUntilRefresh = 120;

        // Honour Retry-After: skip API call but still refresh session stats
        bool skipApi = DateTimeOffset.UtcNow < _apiRetryAfter;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => IsLoading = true);

        try
        {
            UsageResponse? usage = null;
            if (!skipApi)
            {
                usage = await _api.FetchUsageAsync();
                RawApiResponse = _api.LastRawResponse;
                if (_api.LastRetryAfterSeconds > 0)
                    _apiRetryAfter = DateTimeOffset.UtcNow.AddSeconds(_api.LastRetryAfterSeconds);
            }
            var sessionStats = _session.ScanTodayUsage();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                TodayInputTokens  = sessionStats.TotalInputTokens;
                TodayOutputTokens = sessionStats.TotalOutputTokens;
                TodayCacheRead    = sessionStats.TotalCacheReadTokens;
                TodayCacheWrite   = sessionStats.TotalCacheWriteTokens;
                SessionsLabel     = Loc.Sessions(sessionStats.SessionCount);

                // Record today's stats for history
                _history.RecordToday(sessionStats.TotalInputTokens, sessionStats.TotalOutputTokens,
                    sessionStats.TotalCacheReadTokens, sessionStats.TotalCacheWriteTokens,
                    sessionStats.SessionCount);
                HistoryData = _history.GetLast(7);
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

                    // API responded successfully — if 5h usage < 100%, rate limit has cleared
                    if (usage.FiveHour != null && usage.FiveHour.UsagePercent < 1.0)
                    {
                        HasRateLimitHit = false;
                        RateLimitInfo = "";
                    }

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
                        _lastKnownShortPercent = newPercent;
                        _lastKnownShortReset = ShortResetLabel;
                    }

                    if (usage.SevenDay != null)
                    {
                        LongUsagePercent = usage.SevenDay.UsagePercent;
                        LongResetLabel   = FormatResetLabel(usage.SevenDay.ResetsAtParsed);
                        _lastKnownLongPercent = LongUsagePercent;
                        _lastKnownLongReset = LongResetLabel;
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
                else if (skipApi || _api.LastError != null)
                {
                    // Keep last known data visible — don't reset to 0 on transient errors
                    ShortUsagePercent = _lastKnownShortPercent;
                    ShortResetLabel   = _lastKnownShortReset;
                    LongUsagePercent  = _lastKnownLongPercent;
                    LongResetLabel    = _lastKnownLongReset;

                    HasError = true;
                    if (skipApi && _apiRetryAfter > DateTimeOffset.MinValue)
                    {
                        var retryAt = _apiRetryAfter.ToLocalTime().ToString("HH:mm:ss");
                        ErrorMessage = Loc.RateLimitedUntil(retryAt);
                    }
                    else
                    {
                        ErrorMessage = _api.LastError != null
                            ? ParseFriendlyError(_api.LastError)
                            : Loc.RateLimited;
                    }
                    StatusText = "API Error";
                    // LastUpdatedLabel is NOT updated here — it keeps showing the last successful time
                }
                else
                {
                    StatusText = "No data";
                }

                // Always show last attempt time; prefix differs on error so users know data may be stale
                LastUpdatedLabel = HasError
                    ? $"⚠ {DateTime.Now:HH:mm:ss}"
                    : Loc.UpdatedAt(DateTime.Now.ToString("HH:mm:ss"));
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
        _countdownTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
