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

    /// <summary>구독 등급 배지 ("OpenCode Go"). 로그인 항목이 없으면 빈 문자열이라 배지가 숨는다.</summary>
    [ObservableProperty] private string _planLabel = "";
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
    [ObservableProperty] private bool _isUsageEmpty = true;
    [ObservableProperty] private bool _hasStaleSyncedQuota = false;
    [ObservableProperty] private string _syncedQuotaNoticeLabel = "";
    [ObservableProperty] private bool _isWebQuotaUnavailable = false;
    private OpenCodeWebUsage? _currentWebUsage;
    private string? _staleQuotaDevice;
    private DateTimeOffset? _staleQuotaObservedAt;

    public int LastRequestCount => _lastRequestCount;
    public long LastInputTokens => _lastInputTokens;
    public long LastOutputTokens => _lastOutputTokens;
    public ProviderUsageSnapshot LastSnapshot { get; private set; } = new();

    /// <summary>공식 게이지를 못 그리는 상태 — 대신 요청 수·상태 문구를 보여주는 자리에 쓴다.</summary>
    public bool NeedsWebLogin => !HasWebQuota;

    /// <summary>
    /// 로그인 버튼을 실제로 권할 상황인가.
    /// 다른 PC 가 오늘 관측한 공식 값이 있는데 유효시간만 지난 경우라면, 이 PC 는 로그인할 이유가 없다 —
    /// 버튼 대신 마지막 관측 안내를 보여준다. 이 PC 의 세션이 멀쩡한데 확인만 실패한 경우도 마찬가지다.
    /// 관측 이력이 아예 없으면 종전대로 로그인 경로를 남긴다.
    /// </summary>
    public bool ShowWebLoginButton => !HasWebQuota && !HasStaleSyncedQuota && !IsWebQuotaUnavailable;

    /// <summary>버튼을 감춘 자리에 "왜 값이 없는지" 를 알려주는 안내. 동기화 안내가 있으면 그쪽이 우선이다.</summary>
    public bool ShowWebQuotaUnavailableNotice => IsWebQuotaUnavailable && !HasWebQuota && !HasStaleSyncedQuota;
    public string WebQuotaUnavailableLabel => Loc.OpenCodeQuotaUnavailable;
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
            var read = _webUsage == null
                ? OpenCodeWebReadResult.NotAttempted
                : await _webUsage.TryGetUsageAsync();
            var webUsage = read.Usage;
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
                ApplyWebSessionState(read.State, _webUsage?.HasSavedSession == true);
                QuotaStatusLabel = webUsage != null ? Loc.OpenCodeOfficialQuota : FormatQuotaStatus(snapshot.OpenCodeDetails);
                Note = webUsage != null ? Loc.ProviderOpenCodeWebNote : Loc.ProviderOpenCodeNote;
                PlanLabel = PlanLabels.OpenCode(_monitor.GetPlanProviderId());
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
            var read = await _webUsage.TryGetUsageAsync(interactive: true);
            if (read.Usage is not { } usage)
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

        ClearStaleSyncedQuotaNotice();
        ApplyWebUsage(usage);
        QuotaStatusLabel = Loc.OpenCodeOfficialQuota;
        Note = Loc.ProviderOpenCodeWebNote;
        LastSnapshot.OpenCodeDetails ??= new OpenCodeUsageDetails();
        LastSnapshot.OpenCodeDetails.WebUsage = usage;
    }

    /// <summary>
    /// 다른 PC 가 오늘 관측했지만 유효시간이 지나 게이지로는 못 쓰는 공식 값이 있을 때,
    /// 로그인 버튼 대신 보여줄 안내를 세운다. 게이지·퍼센트는 건드리지 않는다.
    /// </summary>
    internal void ApplyStaleSyncedQuotaNotice(string deviceName, DateTimeOffset observedAt)
    {
        if (HasWebQuota)
        {
            ClearStaleSyncedQuotaNotice();
            return;
        }

        _staleQuotaDevice = string.IsNullOrWhiteSpace(deviceName) ? "?" : deviceName;
        _staleQuotaObservedAt = observedAt;
        SyncedQuotaNoticeLabel = Loc.UsageSyncQuotaStale(
            _staleQuotaDevice, observedAt.ToLocalTime().ToString("HH:mm"));
        HasStaleSyncedQuota = true;
    }

    internal void ClearStaleSyncedQuotaNotice()
    {
        _staleQuotaDevice = null;
        _staleQuotaObservedAt = null;
        HasStaleSyncedQuota = false;
        SyncedQuotaNoticeLabel = "";
    }

    /// <summary>
    /// 확인 자체를 못 한 상태(부팅 직후 네트워크 미준비 등)를 로그아웃과 구분해 화면에 옮긴다.
    /// 이 PC 에 이미 성공한 세션이 남아 있는데 확인만 실패했다면 로그인할 이유가 없으므로,
    /// 버튼 대신 재시도 안내를 보여준다. 서버가 로그인 페이지로 되돌렸을 때만 버튼을 남긴다.
    /// </summary>
    internal void ApplyWebSessionState(OpenCodeWebSessionState state, bool hasSavedSession) =>
        IsWebQuotaUnavailable = !HasWebQuota
            && hasSavedSession
            && state == OpenCodeWebSessionState.Unavailable;

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

        // 게이지를 그릴 값이 생겼으니 "왜 못 그리는지" 안내는 모두 필요 없다.
        ClearStaleSyncedQuotaNotice();
        IsWebQuotaUnavailable = false;

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

    partial void OnHasWebQuotaChanged(bool value)
    {
        OnPropertyChanged(nameof(NeedsWebLogin));
        OnPropertyChanged(nameof(ShowWebLoginButton));
        OnPropertyChanged(nameof(ShowWebQuotaUnavailableNotice));
    }

    partial void OnHasStaleSyncedQuotaChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWebLoginButton));
        OnPropertyChanged(nameof(ShowWebQuotaUnavailableNotice));
    }

    partial void OnIsWebQuotaUnavailableChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWebLoginButton));
        OnPropertyChanged(nameof(ShowWebQuotaUnavailableNotice));
    }

    partial void OnIsWebLoginRunningChanged(bool value) => OnPropertyChanged(nameof(WebLoginLabel));

    public void RefreshLocalizedLabels()
    {
        OnPropertyChanged(nameof(RollingTitle));
        OnPropertyChanged(nameof(WeeklyTitle));
        OnPropertyChanged(nameof(MonthlyTitle));
        OnPropertyChanged(nameof(WebLoginLabel));
        OnPropertyChanged(nameof(WebQuotaUnavailableLabel));
        var usage = LastSnapshot.OpenCodeDetails?.WebUsage;
        ApplyWebUsage(usage);
        QuotaStatusLabel = usage != null ? Loc.OpenCodeOfficialQuota : FormatQuotaStatus(LastSnapshot.OpenCodeDetails);
        Note = usage != null ? Loc.ProviderOpenCodeWebNote : Loc.ProviderOpenCodeNote;
        // 언어를 바꿨을 뿐인데 안내 문구만 이전 언어로 남지 않도록 다시 만든다.
        if (usage == null && _staleQuotaDevice is { } device && _staleQuotaObservedAt is { } observedAt)
            SyncedQuotaNoticeLabel = Loc.UsageSyncQuotaStale(device, observedAt.ToLocalTime().ToString("HH:mm"));
    }

    private static string FormatQuotaStatus(OpenCodeUsageDetails? details)
    {
        if (details?.LimitKind == null) return Loc.OpenCodeQuotaNotPublished;
        var reset = details.RetryAt is { } retryAt && retryAt > DateTimeOffset.Now
            ? $" · {Loc.OpenCodeRetryAt(UsageCalculator.FormatResetLabel(retryAt, false, true, DateTimeOffset.Now))}"
            : "";
        return (details.LimitKind == "go" ? Loc.OpenCodeGoLimitReached : Loc.OpenCodeFreeLimitReached) + reset;
    }
}
