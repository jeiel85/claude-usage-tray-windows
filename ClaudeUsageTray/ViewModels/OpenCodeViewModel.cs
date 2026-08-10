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
    [ObservableProperty] private string _fiveHourLabel = "—";
    [ObservableProperty] private string _sevenDayLabel = "—";
    [ObservableProperty] private string _monthLabel = "—";
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
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private bool _isUsageEmpty = true;

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
                FiveHourLabel = FormatPeriod(snapshot.OpenCodeDetails?.LastFiveHours);
                SevenDayLabel = FormatPeriod(snapshot.OpenCodeDetails?.LastSevenDays);
                MonthLabel = FormatPeriod(snapshot.OpenCodeDetails?.ThisMonth);
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

    private void ApplyWebUsage(OpenCodeWebUsage? usage)
    {
        HasWebQuota = usage != null;
        if (usage == null)
        {
            RollingPercent = WeeklyPercent = MonthlyPercent = 0;
            RollingResetLabel = WeeklyResetLabel = MonthlyResetLabel = "";
            Percent = 0;
            return;
        }

        RollingPercent = usage.Rolling.UsagePercent;
        WeeklyPercent = usage.Weekly.UsagePercent;
        MonthlyPercent = usage.Monthly.UsagePercent;
        RollingResetLabel = Loc.OpenCodeResetAt(UsageCalculator.FormatResetLabel(
            usage.Rolling.ResetAt, false, true, DateTimeOffset.Now));
        WeeklyResetLabel = Loc.OpenCodeResetAt(UsageCalculator.FormatResetLabel(
            usage.Weekly.ResetAt, false, true, DateTimeOffset.Now));
        MonthlyResetLabel = Loc.OpenCodeResetAt(UsageCalculator.FormatResetLabel(
            usage.Monthly.ResetAt, false, true, DateTimeOffset.Now));
        Percent = RollingPercent;
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

    private static string FormatPeriod(OpenCodePeriodUsage? period)
    {
        if (period == null || period.Requests == 0) return "—";
        var cost = period.CostUsd > 0 ? $" · ${period.CostUsd:0.00}" : "";
        return $"{UsageCalculator.FormatTokenShort(period.Tokens)} · {period.Requests}{Loc.OpenCodeRequestUnit}{cost}";
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
