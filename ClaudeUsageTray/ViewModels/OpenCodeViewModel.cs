using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class OpenCodeViewModel : ObservableObject
{
    private readonly OpenCodeUsageMonitor _monitor;
    private readonly HistoryService _history;
    private readonly OpenCodeWebUsageService? _webUsage;
    private int _lastRequestCount = 0;
    private long _lastInputTokens = 0;
    private long _lastOutputTokens = 0;

    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _note = Loc.ProviderOpenCodeNote;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _inputLabel = "";
    [ObservableProperty] private string _outputLabel = "";
    [ObservableProperty] private string _requestCountLabel = "";
    [ObservableProperty] private string _cacheReadLabel = "—";
    [ObservableProperty] private string _cacheWriteLabel = "—";
    [ObservableProperty] private double _percent = 0;
    [ObservableProperty] private string _quotaStatusLabel = "";
    [ObservableProperty] private bool _hasPeriodUsage = false;
    [ObservableProperty] private bool _hasWebQuota = false;
    [ObservableProperty] private bool _isWebLoginRunning = false;
    [ObservableProperty] private string _webLoginError = "";
    [ObservableProperty] private double _rollingPercent = 0;
    [ObservableProperty] private double _weeklyPercent = 0;
    [ObservableProperty] private double _monthlyPercent = 0;
    [ObservableProperty] private string _rollingResetLabel = "";
    [ObservableProperty] private string _weeklyResetLabel = "";
    [ObservableProperty] private string _monthlyResetLabel = "";
    [ObservableProperty] private bool _hasRollingTimeline = false;
    [ObservableProperty] private bool _hasWeeklyTimeline = false;
    [ObservableProperty] private bool _hasMonthlyTimeline = false;
    [ObservableProperty] private double _rollingTimePercent = 0;
    [ObservableProperty] private double _weeklyTimePercent = 0;
    [ObservableProperty] private double _monthlyTimePercent = 0;
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private bool _isUsageEmpty = true;
    private OpenCodeWebUsage? _currentWebUsage;

    public int LastRequestCount => _lastRequestCount;
    public long LastInputTokens => _lastInputTokens;
    public long LastOutputTokens => _lastOutputTokens;
    public ProviderUsageSnapshot LastSnapshot { get; private set; } = new();

    public bool NeedsWebLogin => !HasWebQuota;
    public string RollingTitle => Loc.OpenCodeRollingUsage;
    public string WeeklyTitle => Loc.OpenCodeWeeklyUsage;
    public string MonthlyTitle => Loc.OpenCodeMonthlyUsage;
    public string WebLoginLabel => IsWebLoginRunning ? Loc.OpenCodeConnectingWeb : Loc.OpenCodeConnectWeb;

    public OpenCodeViewModel(OpenCodeUsageMonitor monitor, HistoryService history,
        OpenCodeWebUsageService? webUsage = null)
    {
        _monitor = monitor;
        _history = history;
        _webUsage = webUsage;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var snapshot = _monitor.GetTodaySnapshot();
            var webUsage = _webUsage == null ? null : await _webUsage.TryGetUsageAsync();
            if (snapshot.OpenCodeDetails != null) snapshot.OpenCodeDetails.WebUsage = webUsage;
            LastSnapshot = snapshot;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var informational = UsageCalculator.IsNoUsageInformational(snapshot.ErrorMessage, UsageProviderKind.OpenCode);
                HasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) && !informational;
                ErrorMessage = informational ? "" : (snapshot.ErrorMessage ?? "");

                _lastRequestCount = snapshot.RequestCount;
                _lastInputTokens  = snapshot.TotalInputTokens;
                _lastOutputTokens = snapshot.TotalOutputTokens;

                RequestCountLabel = snapshot.RequestCount > 0
                    ? Loc.CurrentLang == "ko" ? $"{snapshot.RequestCount}회" : $"{snapshot.RequestCount} req"
                    : "—";
                InputLabel      = snapshot.TotalInputTokens      > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalInputTokens)      : "—";
                OutputLabel     = snapshot.TotalOutputTokens     > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalOutputTokens)     : "—";
                CacheReadLabel  = snapshot.TotalCacheReadTokens  > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalCacheReadTokens)  : "—";
                CacheWriteLabel = snapshot.TotalCacheWriteTokens > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalCacheWriteTokens) : "—";
                Summary = snapshot.HasData
                    ? Loc.CurrentLang == "ko"
                        ? $"오늘 {snapshot.RequestCount}회 · 입력 {UsageCalculator.FormatTokenShort(snapshot.TotalInputTokens)} · 출력 {UsageCalculator.FormatTokenShort(snapshot.TotalOutputTokens)}"
                        : $"Today {snapshot.RequestCount} req · in {UsageCalculator.FormatTokenShort(snapshot.TotalInputTokens)} · out {UsageCalculator.FormatTokenShort(snapshot.TotalOutputTokens)}"
                    : snapshot.ErrorMessage ?? "";
                ApplyWebUsage(webUsage);
                QuotaStatusLabel = webUsage != null ? Loc.OpenCodeOfficialQuota : FormatQuotaStatus(snapshot.OpenCodeDetails);
                Note = webUsage != null ? Loc.ProviderOpenCodeWebNote : Loc.ProviderOpenCodeNote;
                HasPeriodUsage = snapshot.OpenCodeDetails?.ThisMonth.Requests > 0;
                IsUsageEmpty = !snapshot.HasData;

                _history.RecordToday(UsageProviderKind.OpenCode, null,
                    snapshot.TotalInputTokens, snapshot.TotalOutputTokens,
                    snapshot.TotalCacheReadTokens, snapshot.TotalCacheWriteTokens,
                    snapshot.SessionCount);
            });
        }
        catch (Exception ex)
        {
            LastSnapshot = new ProviderUsageSnapshot { ErrorMessage = ex.Message };
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasError = true;
                ErrorMessage = ex.Message;
            });
        }
    }

    public async Task<bool> ConnectWebUsageAsync()
    {
        if (_webUsage == null) return false;
        IsWebLoginRunning = true;
        WebLoginError = "";
        try
        {
            var usage = await _webUsage.TryGetUsageAsync(interactive: true);
            if (usage == null)
            {
                WebLoginError = _webUsage.LastError ?? Loc.OpenCodeWebLoginCancelled;
                return false;
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyWebUsage(usage);
                QuotaStatusLabel = Loc.OpenCodeOfficialQuota;
                Note = Loc.ProviderOpenCodeWebNote;
                if (LastSnapshot.OpenCodeDetails != null) LastSnapshot.OpenCodeDetails.WebUsage = usage;
            });
            return true;
        }
        finally
        {
            IsWebLoginRunning = false;
        }
    }

    /// <summary>
    /// 현재 PC에서 공식 값을 읽지 못했을 때만 다른 PC가 관측한 공식 할당량을 적용한다.
    /// 로그인 상태나 로컬 웹 세션은 변경하지 않는다.
    /// </summary>
    internal void ApplySyncedWebUsage(OpenCodeWebUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);
        if (HasWebQuota) return;

        ApplyWebUsage(usage);
        QuotaStatusLabel = Loc.OpenCodeOfficialQuota;
        Note = Loc.ProviderOpenCodeWebNote;
        LastSnapshot.OpenCodeDetails ??= new OpenCodeUsageDetails();
        LastSnapshot.OpenCodeDetails.WebUsage = usage;
    }

    private void ApplyWebUsage(OpenCodeWebUsage? usage)
    {
        _currentWebUsage = usage;
        HasWebQuota = usage != null;
        if (usage == null)
        {
            RollingPercent = WeeklyPercent = MonthlyPercent = 0;
            RollingResetLabel = WeeklyResetLabel = MonthlyResetLabel = "";
            HasRollingTimeline = HasWeeklyTimeline = HasMonthlyTimeline = false;
            RollingTimePercent = WeeklyTimePercent = MonthlyTimePercent = 0;
            Percent = 0;
            return;
        }

        var now = DateTimeOffset.Now;
        RollingPercent = usage.Rolling.UsagePercent;
        WeeklyPercent = usage.Weekly.UsagePercent;
        MonthlyPercent = usage.Monthly.UsagePercent;
        RollingResetLabel = UsageCalculator.FormatResetLabel(
            usage.Rolling.ResetAt, false, true, now);
        WeeklyResetLabel = UsageCalculator.FormatResetLabel(
            usage.Weekly.ResetAt, false, true, now);
        MonthlyResetLabel = UsageCalculator.FormatResetLabel(
            usage.Monthly.ResetAt, false, true, now);
        Percent = RollingPercent;
        UpdateTimeProgress(now);
    }

    public void UpdateTimeProgress(DateTimeOffset now)
    {
        if (_currentWebUsage == null) return;
        var timeline = CalculateTimeProgress(_currentWebUsage, now);
        HasRollingTimeline = timeline.Rolling.HasValue;
        HasWeeklyTimeline = timeline.Weekly.HasValue;
        HasMonthlyTimeline = timeline.Monthly.HasValue;
        RollingTimePercent = timeline.Rolling ?? 0;
        WeeklyTimePercent = timeline.Weekly ?? 0;
        MonthlyTimePercent = timeline.Monthly ?? 0;
    }

    internal static (double? Rolling, double? Weekly, double? Monthly) CalculateTimeProgress(
        OpenCodeWebUsage usage, DateTimeOffset now)
    {
        var monthlyStart = usage.Monthly.ResetAt.AddMonths(-1);
        return (
            UsageCalculator.TimeProgress(usage.Rolling.ResetAt, TimeSpan.FromHours(5), now),
            UsageCalculator.TimeProgress(usage.Weekly.ResetAt, TimeSpan.FromDays(7), now),
            UsageCalculator.TimeProgress(usage.Monthly.ResetAt, usage.Monthly.ResetAt - monthlyStart, now));
    }

    partial void OnHasWebQuotaChanged(bool value) => OnPropertyChanged(nameof(NeedsWebLogin));
    partial void OnIsWebLoginRunningChanged(bool value) => OnPropertyChanged(nameof(WebLoginLabel));

    public void RefreshLocalizedLabels()
    {
        OnPropertyChanged(nameof(RollingTitle));
        OnPropertyChanged(nameof(WeeklyTitle));
        OnPropertyChanged(nameof(MonthlyTitle));
        OnPropertyChanged(nameof(WebLoginLabel));
        var usage = LastSnapshot.OpenCodeDetails?.WebUsage;
        ApplyWebUsage(usage);
        QuotaStatusLabel = usage != null ? Loc.OpenCodeOfficialQuota : FormatQuotaStatus(LastSnapshot.OpenCodeDetails);
        Note = usage != null ? Loc.ProviderOpenCodeWebNote : Loc.ProviderOpenCodeNote;
    }

    private static string FormatQuotaStatus(OpenCodeUsageDetails? details)
    {
        if (details?.LimitKind == null) return Loc.OpenCodeQuotaNotPublished;
        var reset = details.RetryAt is { } retryAt && retryAt > DateTimeOffset.Now
            ? $" · {Loc.OpenCodeRetryAt(UsageCalculator.FormatResetLabel(retryAt, false, true, DateTimeOffset.Now))}"
            : "";
        return (details.LimitKind == "go" ? Loc.OpenCodeGoLimitReached : Loc.OpenCodeFreeLimitReached) + reset;
    }

    public void UpdateActiveState(bool isEnabled, bool hideInactive)
    {
        IsActive = isEnabled && (!hideInactive || _lastRequestCount > 0 || HasPeriodUsage || HasWebQuota || HasError);
    }
}
