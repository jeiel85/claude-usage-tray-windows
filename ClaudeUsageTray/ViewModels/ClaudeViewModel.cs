using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class ClaudeViewModel : ObservableObject
{
    private readonly UsageApiService _api;
    private readonly CredentialService _credentials;
    private readonly SessionMonitor _session;
    private readonly HistoryService _history;
    private double _prevShortPercent = -1;
    private double _prevExtraPercent = -1;
    private bool _prevHadRateLimit = false;
    private string _prevShortDepletion = "";
    private DateTimeOffset? _lastNotifiedEarlyResetAt;
    private double _lastKnownShortPercent = 0;
    private double _lastKnownLongPercent = 0;
    private string _lastKnownShortReset = "";
    private string _lastKnownLongReset = "";
    private DateTimeOffset _apiRetryAfter = DateTimeOffset.MinValue;
    private string? _lastKnownOrgUuid;
    private DateTimeOffset? _rawShortResetAt;
    private DateTimeOffset? _rawLongResetAt;

    [ObservableProperty] private double _shortPercent = 0;
    [ObservableProperty] private string _shortReset = "";
    [ObservableProperty] private double _longPercent = 0;
    [ObservableProperty] private string _longReset = "";
    [ObservableProperty] private string _shortSummary = "";
    [ObservableProperty] private string _longSummary = "";
    [ObservableProperty] private string _shortDepletion = "";
    [ObservableProperty] private string _longDepletion = "";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _apiNote = "";
    [ObservableProperty] private long _todayInputTokens = 0;
    [ObservableProperty] private long _todayOutputTokens = 0;
    [ObservableProperty] private long _todayCacheRead = 0;
    [ObservableProperty] private long _todayCacheWrite = 0;
    [ObservableProperty] private string _sessionsLabel = "";
    [ObservableProperty] private bool _hasRateLimitHit = false;
    [ObservableProperty] private string _rateLimitInfo = "";
    [ObservableProperty] private bool _extraUsageEnabled = false;
    [ObservableProperty] private bool _extraHasLimit = false;
    [ObservableProperty] private double _extraUsagePercent = 0;
    [ObservableProperty] private string _extraCreditsLabel = "";
    [ObservableProperty] private bool _isExtraOnlyMode = false;
    [ObservableProperty] private IReadOnlyList<DailyStats> _historyData = [];
    [ObservableProperty] private long[] _hourlyTokens = new long[24];
    [ObservableProperty] private string _todayCostLabel = "";
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private bool _isUsageEmpty = true;
    [ObservableProperty] private bool _isSubscribed = false;

    public double PrevShortPercent => _prevShortPercent;
    public double PrevExtraPercent => _prevExtraPercent;
    public string HistoryChartTitle => Loc.HistoryTitleFor("Claude");
    public string? RawApiResponse { get; private set; }

    public ClaudeViewModel(UsageApiService api, CredentialService credentials, SessionMonitor session, HistoryService history)
    {
        _api = api;
        _credentials = credentials;
        _session = session;
        _history = history;
    }

    public async Task RefreshAsync(bool showAbsoluteResetTime, string ntfyTopic, bool notificationsEnabled, bool notifyRateLimit, bool notifyOnQuotaReset, int[] thresholds, Action<int, string, string, string> showUsageAlert, Action showQuotaResetAlert, Action<string, string, string> showEarlyExhaustionAlert, Action<string> showRateLimitAlert)
    {
        var currentOrgUuid = _credentials.GetOrganizationUuid();
        if (currentOrgUuid != _lastKnownOrgUuid)
        {
            _lastKnownOrgUuid = currentOrgUuid;
            _history.SetScope(UsageProviderKind.Claude, currentOrgUuid);
            _apiRetryAfter = DateTimeOffset.MinValue;
        }

        bool skipApi = DateTimeOffset.UtcNow < _apiRetryAfter;

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

                _history.RecordToday(sessionStats.TotalInputTokens, sessionStats.TotalOutputTokens,
                    sessionStats.TotalCacheReadTokens, sessionStats.TotalCacheWriteTokens,
                    sessionStats.SessionCount);
                
                HistoryData = _history.GetLast(7);
                HourlyTokens = sessionStats.HourlyTokens;

                TodayCostLabel = UsageCalculator.CalcCostLabel(sessionStats.TotalInputTokens,
                    sessionStats.TotalOutputTokens,
                    sessionStats.TotalCacheReadTokens,
                    sessionStats.TotalCacheWriteTokens);
                HasRateLimitHit   = sessionStats.HasRateLimitHit;
                RateLimitInfo     = sessionStats.RateLimitResetTime ?? "";

                if (notificationsEnabled && notifyRateLimit &&
                    sessionStats.HasRateLimitHit && !_prevHadRateLimit)
                {
                    showRateLimitAlert(ntfyTopic);
                }
                _prevHadRateLimit = sessionStats.HasRateLimitHit;

                IsUsageEmpty = TodayInputTokens + TodayOutputTokens == 0;

                if (usage?.FiveHour != null || usage?.SevenDay != null)
                {
                    HasError = false;
                    ErrorMessage = "";
                    ApiNote = "";

                    if (usage.FiveHour != null && usage.FiveHour.UsagePercent < 1.0)
                    {
                        HasRateLimitHit = false;
                        RateLimitInfo = "";
                    }

                    if (usage!.FiveHour != null)
                    {
                        var newPercent = usage.FiveHour.UsagePercent;
                        _rawShortResetAt = usage.FiveHour.ResetsAtParsed;
                        ShortReset = UsageCalculator.FormatResetLabel(_rawShortResetAt, false, showAbsoluteResetTime, DateTimeOffset.Now);
                        ShortSummary = Loc.UsageSummary(newPercent);
                        ShortDepletion = UsageCalculator.CalcDepletionLabel(usage.FiveHour, DateTimeOffset.Now);

                        if (notificationsEnabled && !string.IsNullOrEmpty(ShortDepletion))
                        {
                            var currentReset = usage.FiveHour.ResetsAtParsed;
                            bool isNewCycle = _lastNotifiedEarlyResetAt != currentReset;
                            bool isNewDetection = string.IsNullOrEmpty(_prevShortDepletion);

                            if (isNewCycle || isNewDetection)
                            {
                                if (currentReset.HasValue)
                                {
                                    var windowStart = currentReset.Value - TimeSpan.FromHours(5);
                                    var elapsed = DateTimeOffset.Now - windowStart;
                                    if (elapsed.TotalMinutes >= 5)
                                    {
                                        double ratePerHour = usage.FiveHour.UsagePercent / elapsed.TotalHours;
                                        if (ratePerHour > 0)
                                        {
                                            double hoursToFull = (1.0 - usage.FiveHour.UsagePercent) / ratePerHour;
                                            var depletionAt = DateTimeOffset.Now.AddHours(hoursToFull).ToLocalTime();
                                            showEarlyExhaustionAlert(
                                                depletionAt.ToString("HH:mm"),
                                                UsageCalculator.FormatResetLabel(currentReset, false, showAbsoluteResetTime, DateTimeOffset.Now),
                                                ntfyTopic);
                                            _lastNotifiedEarlyResetAt = currentReset;
                                        }
                                    }
                                }
                            }
                        }
                        _prevShortDepletion = ShortDepletion;

                        if (notificationsEnabled && _prevShortPercent >= 0)
                        {
                            if (notifyOnQuotaReset && _prevShortPercent >= 1.0 && newPercent < 1.0)
                            {
                                showQuotaResetAlert();
                            }

                            foreach (var t in thresholds.OrderBy(x => x))
                            {
                                double tf = t / 100.0;
                                if (_prevShortPercent < tf && newPercent >= tf)
                                {
                                    showUsageAlert(t, Loc.FiveHourWindow, ShortReset, ntfyTopic);
                                }
                            }
                        }

                        ShortPercent = newPercent;
                        _prevShortPercent = newPercent;
                        _lastKnownShortPercent = newPercent;
                        _lastKnownShortReset = ShortReset;
                    }

                    if (usage.SevenDay != null)
                    {
                        LongPercent    = usage.SevenDay.UsagePercent;
                        _rawLongResetAt = usage.SevenDay.ResetsAtParsed;
                        LongReset      = UsageCalculator.FormatResetLabel(_rawLongResetAt, false, showAbsoluteResetTime, DateTimeOffset.Now);
                        LongSummary    = Loc.UsageSummary(LongPercent);
                        LongDepletion  = UsageCalculator.CalcLongDepletionLabel(usage.SevenDay, DateTimeOffset.Now);
                        _lastKnownLongPercent = LongPercent;
                        _lastKnownLongReset = LongReset;
                    }

                    if (usage.ExtraUsage is { IsEnabled: true } eu)
                    {
                        ExtraUsageEnabled = true;
                        ExtraHasLimit     = eu.MonthlyLimit.HasValue;
                        ExtraUsagePercent = eu.MonthlyLimit.HasValue
                            ? Math.Min(1.0, (eu.Utilization ?? 0) / 100.0)
                            : 0;
                        ExtraCreditsLabel = (eu.UsedCredits.HasValue && eu.MonthlyLimit.HasValue)
                            ? Loc.ExtraCredits(eu.UsedCredits.Value, eu.MonthlyLimit.Value)
                            : eu.UsedCredits.HasValue
                                ? Loc.ExtraCreditsUsedOnly(eu.UsedCredits.Value)
                                : "";

                        if (_prevExtraPercent < 0) _prevExtraPercent = ExtraUsagePercent;
                    }
                    else
                    {
                        ExtraUsageEnabled = false;
                    }

                    if (ExtraUsageEnabled && ShortPercent >= 1.0)
                    {
                        IsExtraOnlyMode = true;
                    }
                    else if (ShortPercent < 0.5)
                    {
                        IsExtraOnlyMode = false;
                    }
                }
                else if (skipApi || _api.LastError != null)
                {
                    ShortPercent = _lastKnownShortPercent;
                    ShortReset   = _lastKnownShortReset;
                    ShortSummary = Loc.UsageSummary(_lastKnownShortPercent);
                    LongPercent  = _lastKnownLongPercent;
                    LongReset    = _lastKnownLongReset;
                    LongSummary  = Loc.UsageSummary(_lastKnownLongPercent);

                    bool isPermissionDenied = _api.LastError != null
                        && _api.LastError.Contains("HTTP 403")
                        && _api.LastError.Contains("permission_error");

                    bool isOAuthNotAllowed = isPermissionDenied
                        && _api.LastError!.Contains("currently not allowed");

                    bool isCooldown = _apiRetryAfter > DateTimeOffset.UtcNow;

                    if (isPermissionDenied)
                    {
                        HasError = false;
                        ErrorMessage = "";
                        if (isOAuthNotAllowed)
                        {
                            ApiNote = Loc.ApiOAuthNotAllowedNote;
                        }
                        else
                        {
                            ApiNote = Loc.ApiPermissionDeniedNote;
                        }
                    }
                    else if (isCooldown)
                    {
                        HasError = false;
                        ErrorMessage = "";
                        ApiNote = Loc.ApiCooldownNote(_apiRetryAfter.ToLocalTime().ToString("HH:mm"));
                    }
                    else
                    {
                        HasError = true;
                        // 토큰 자체가 없어 네트워크 호출 전에 실패한 경우 — 막연한 원문 대신 로그인 안내로.
                        ErrorMessage = _api.LastError == UsageApiService.NoTokenError
                            ? Loc.NoToken
                            : _api.LastError != null
                                ? ParseFriendlyError(_api.LastError)
                                : Loc.RateLimited;
                        ApiNote = "";
                    }
                }
            });
        }
        catch (Exception ex)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = ex.Message;
                ApiNote = "";
            });
        }
    }

    public void UpdateActiveState(bool isEnabled, bool hideInactive)
    {
        IsActive = isEnabled && (!hideInactive || TodayInputTokens + TodayOutputTokens > 0 || ShortPercent > 0 || HasError);
    }

    public void UpdateResetLabels(bool showAbsoluteResetTime)
    {
        var now = DateTimeOffset.Now;
        if (_rawShortResetAt.HasValue && (_rawShortResetAt.Value - now).TotalMinutes < 10)
            ShortReset = UsageCalculator.FormatResetLabel(_rawShortResetAt, false, showAbsoluteResetTime, now);
        if (_rawLongResetAt.HasValue && (_rawLongResetAt.Value - now).TotalMinutes < 10)
            LongReset = UsageCalculator.FormatResetLabel(_rawLongResetAt, false, showAbsoluteResetTime, now);
    }

    public void UpdateSubscription()
    {
        var subType = _credentials.GetSubscriptionType();
        IsSubscribed = !string.IsNullOrEmpty(subType)
            && !string.Equals(subType, "free", StringComparison.OrdinalIgnoreCase);
    }

    public void ResetRateLimitState()
    {
        _apiRetryAfter = DateTimeOffset.MinValue;
    }

    internal static string ParseFriendlyError(string raw)
    {
        if (raw.Contains("429") || raw.Contains("rate_limit"))
            return Loc.RateLimited;
        return Loc.ApiError(raw.Length > 200 ? raw[..200] + "…" : raw);
    }
}
