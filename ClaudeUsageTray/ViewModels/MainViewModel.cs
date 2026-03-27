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
    [ObservableProperty] private string _lastUpdated = "Never";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";

    // 5h window
    [ObservableProperty] private double _shortUsagePercent = 0;
    [ObservableProperty] private long _shortUsed = 0;
    [ObservableProperty] private long _shortMax = 0;
    [ObservableProperty] private string _shortResetTime = "";

    // 7d window
    [ObservableProperty] private double _longUsagePercent = 0;
    [ObservableProperty] private long _longUsed = 0;
    [ObservableProperty] private long _longMax = 0;
    [ObservableProperty] private string _longResetTime = "";

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
    [ObservableProperty] private int _todaySessionCount = 0;
    [ObservableProperty] private bool _hasRateLimitHit = false;
    [ObservableProperty] private string _rateLimitInfo = "";

    // Raw response for debugging
    public string? RawApiResponse { get; private set; }

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
                TodaySessionCount = sessionStats.SessionCount;
                HasRateLimitHit = sessionStats.HasRateLimitHit;
                RateLimitInfo = sessionStats.RateLimitResetTime ?? "";

                if (usage != null && usage.Usage.Count > 0)
                {
                    HasError = false;
                    foreach (var bucket in usage.Usage)
                    {
                        if (bucket.Bucket == "5h")
                        {
                            ShortUsagePercent = bucket.UsagePercent;
                            ShortUsed = bucket.UsedAmount;
                            ShortMax = bucket.MaxCredits ?? 0;
                            ShortResetTime = FormatReset(bucket.ResetsAt);
                        }
                        else if (bucket.Bucket == "7d")
                        {
                            LongUsagePercent = bucket.UsagePercent;
                            LongUsed = bucket.UsedAmount;
                            LongMax = bucket.MaxCredits ?? 0;
                            LongResetTime = FormatReset(bucket.ResetsAt);

                            if (bucket.ModelUsage != null)
                            {
                                var totalModel = bucket.ModelUsage.Values.Sum();
                                foreach (var (model, count) in bucket.ModelUsage)
                                {
                                    var lower = model.ToLowerInvariant();
                                    if (lower.Contains("opus"))
                                    {
                                        OpusTokens = count;
                                        OpusPercent = totalModel > 0 ? (double)count / totalModel : 0;
                                    }
                                    else if (lower.Contains("sonnet"))
                                    {
                                        SonnetTokens = count;
                                        SonnetPercent = totalModel > 0 ? (double)count / totalModel : 0;
                                    }
                                }
                            }
                        }
                    }

                    StatusText = ShortMax > 0
                        ? $"{ShortUsagePercent:P0} used"
                        : "Connected";
                }
                else if (_api.LastError != null)
                {
                    HasError = true;
                    ErrorMessage = _api.LastError.Length > 80
                        ? _api.LastError[..80] + "..."
                        : _api.LastError;
                    StatusText = "API Error";
                }
                else
                {
                    StatusText = "No data";
                }

                LastUpdated = DateTime.Now.ToString("HH:mm:ss");
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

    private static string FormatReset(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return "";
        var diff = resetAt.Value - DateTimeOffset.Now;
        if (diff.TotalSeconds <= 0) return "Now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h {diff.Minutes}m";
        return $"{(int)diff.TotalDays}d {diff.Hours}h";
    }

    public void Dispose()
    {
        _timer.Dispose();
        GC.SuppressFinalize(this);
    }
}
