using System.Text.Json;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Timer = System.Timers.Timer;

namespace ClaudeUsageTray.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly UsageApiService _api;
    private readonly SessionMonitor _session;
    private readonly Timer _timer;

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

    // Raw response for debugging
    public string? RawApiResponse { get; private set; }

    // Localized static labels (read-once at startup)
    public string LblAppTitle    => Loc.AppTitle;
    public string LblApiQuota    => Loc.ApiQuota;
    public string LblTodayTokens => Loc.TodayTokens;
    public string LblFiveHour    => Loc.FiveHourWindow;
    public string LblSevenDay    => Loc.SevenDayWindow;
    public string LblInput       => Loc.Input;
    public string LblOutput      => Loc.Output;
    public string LblCacheRead   => Loc.CacheRead;
    public string LblCacheWrite  => Loc.CacheWrite;
    public string LblTokens      => Loc.Tokens;
    public string LblRefresh     => Loc.Refresh;
    public string LblQuit        => Loc.Quit;
    public string LblRefreshing  => Loc.Refreshing;

    public MainViewModel(UsageApiService api, SessionMonitor session)
    {
        _api = api;
        _session = session;

        _timer = new Timer(30_000); // 30 second refresh
        _timer.Elapsed += async (_, _) => await RefreshAsync();
        _timer.AutoReset = true;
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
            // Fetch API usage
            var usage = await _api.FetchUsageAsync();
            RawApiResponse = _api.LastRawResponse;

            // Scan local sessions
            var sessionStats = _session.ScanTodayUsage();

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Update session stats
                TodayInputTokens = sessionStats.TotalInputTokens;
                TodayOutputTokens = sessionStats.TotalOutputTokens;
                TodayCacheRead = sessionStats.TotalCacheReadTokens;
                TodayCacheWrite = sessionStats.TotalCacheWriteTokens;
                SessionsLabel = Loc.Sessions(sessionStats.SessionCount);
                HasRateLimitHit = sessionStats.HasRateLimitHit;
                RateLimitInfo = sessionStats.RateLimitResetTime ?? "";

                if (usage?.FiveHour != null || usage?.SevenDay != null)
                {
                    HasError = false;

                    if (usage.FiveHour != null)
                    {
                        ShortUsagePercent = usage.FiveHour.UsagePercent;
                        ShortResetLabel = FormatResetLabel(usage.FiveHour.ResetsAtParsed);
                    }

                    if (usage.SevenDay != null)
                    {
                        LongUsagePercent = usage.SevenDay.UsagePercent;
                        LongResetLabel = FormatResetLabel(usage.SevenDay.ResetsAtParsed);
                    }

                    if (usage.SevenDayOpus != null)
                    {
                        OpusPercent = usage.SevenDayOpus.UsagePercent;
                        OpusTokens = (long)(usage.SevenDayOpus.Utilization);
                    }

                    if (usage.SevenDaySonnet != null)
                    {
                        SonnetPercent = usage.SevenDaySonnet.UsagePercent;
                        SonnetTokens = (long)(usage.SevenDaySonnet.Utilization);
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
        // 429 rate limit
        if (raw.Contains("429") || raw.Contains("rate_limit"))
            return Loc.RateLimited;

        // Try to extract just the "message" field from JSON error
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
        catch { /* ignore parse failures */ }

        return Loc.ApiError(raw.Length > 80 ? raw[..80] + "…" : raw);
    }

    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
