using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class CodexViewModel : ObservableObject
{
    private readonly CodexUsageMonitor _monitor;
    private readonly HistoryService _history;
    private double _prevPercent = -1;
    private DateTimeOffset? _rawShortResetAt;
    private bool _rawShortResetEstimated;
    private DateTimeOffset? _rawLongResetAt;
    private int? _rawShortWindowMinutes;
    private int? _rawLongWindowMinutes;

    [ObservableProperty] private double _percent = 0;
    [ObservableProperty] private string _reset = "";
    [ObservableProperty] private string _dataSource = "";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _note = Loc.ProviderCodexNote;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private double _longPercent = 0;
    [ObservableProperty] private string _longReset = "";
    [ObservableProperty] private string _longSummary = "";
    [ObservableProperty] private bool _isLongVisible = false;
    [ObservableProperty] private string _planLabel = "ChatGPT plan";
    [ObservableProperty] private string _shortWindowLabel = Loc.ShortWindow;
    [ObservableProperty] private string _longWindowLabel = Loc.LongWindow;
    [ObservableProperty] private string _inputLabel = "—";
    [ObservableProperty] private string _outputLabel = "—";
    [ObservableProperty] private string _cacheReadLabel = "—";
    [ObservableProperty] private string _cacheWriteLabel = "—";
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private bool _isUsageEmpty = true;

    public double PrevPercent => _prevPercent;
    public DateTimeOffset? RawShortResetAt => _rawShortResetAt;
    public bool RawShortResetEstimated => _rawShortResetEstimated;
    public DateTimeOffset? RawLongResetAt => _rawLongResetAt;
    // 창 길이(window_minutes)는 시간선 마커 위치 계산에 필요하다 — Codex 는 5시간/주간 등 창이 계정마다 다르다.
    public int? RawShortWindowMinutes => _rawShortWindowMinutes;
    public int? RawLongWindowMinutes => _rawLongWindowMinutes;
    public ProviderUsageSnapshot LastSnapshot { get; private set; } = new();

    public CodexViewModel(CodexUsageMonitor monitor, HistoryService history)
    {
        _monitor = monitor;
        _history = history;
    }

    public async Task RefreshAsync(bool showAbsoluteResetTime, string ntfyTopic, bool notificationsEnabled, bool notifyOnQuotaReset, Action<int, string, string, string> showUsageAlert, Action showQuotaResetAlert)
    {
        try
        {
            var snapshot = await _monitor.GetTodaySnapshotAsync();
            LastSnapshot = snapshot;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var newPercent = snapshot.ShortUsagePercent;
                _rawShortResetAt = snapshot.ShortResetAt;
                _rawShortResetEstimated = snapshot.IsShortResetEstimated;
                _rawShortWindowMinutes = snapshot.ShortWindowMinutes;
                _rawLongWindowMinutes = snapshot.LongWindowMinutes;
                Reset = UsageCalculator.FormatResetLabel(_rawShortResetAt, _rawShortResetEstimated, showAbsoluteResetTime, DateTimeOffset.Now);
                DataSource = snapshot.DataSource ?? "";
                var informational = UsageCalculator.IsNoUsageInformational(snapshot.ErrorMessage, UsageProviderKind.Codex);
                HasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) && !informational;
                ErrorMessage = informational ? "" : (snapshot.ErrorMessage ?? "");
                Summary = Loc.UsageSummary(newPercent);

                if (notificationsEnabled && _prevPercent >= 0)
                {
                    if (notifyOnQuotaReset && _prevPercent >= 1.0 && newPercent < 1.0)
                    {
                        showQuotaResetAlert();
                    }

                    var providerLabel = UsageProviderKind.DisplayName(UsageProviderKind.Codex);
                    foreach (var t in new[] { 50, 75, 90, 100 }.OrderBy(x => x))
                    {
                        double tf = t / 100.0;
                        if (_prevPercent < tf && newPercent >= tf)
                        {
                            showUsageAlert(t, Loc.Usage, Reset, ntfyTopic);
                        }
                    }
                }

                Percent = newPercent;
                _prevPercent = newPercent;

                LongPercent   = snapshot.LongUsagePercent;
                _rawLongResetAt = snapshot.LongResetAt;
                LongReset     = UsageCalculator.FormatResetLabel(_rawLongResetAt, false, showAbsoluteResetTime, DateTimeOffset.Now);
                LongSummary   = Loc.UsageSummary(snapshot.LongUsagePercent);
                // 창 라벨은 window_minutes 로 결정(주간/5시간 등). 창 길이를 모르면 폴백.
                ShortWindowLabel = Loc.CodexWindowLabel(snapshot.ShortWindowMinutes);
                LongWindowLabel  = Loc.CodexWindowLabel(snapshot.LongWindowMinutes);
                // 장기(둘째) 창은 실제로 두 번째 창이 존재할 때만 노출.
                IsLongVisible = snapshot.LongWindowMinutes is not null || snapshot.LongUsagePercent > 0 || snapshot.LongResetAt is not null;

                PlanLabel = !string.IsNullOrWhiteSpace(snapshot.PlanType)
                    ? $"ChatGPT {snapshot.PlanType}"
                    : "ChatGPT plan";

                InputLabel      = snapshot.TotalInputTokens      > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalInputTokens)      : "—";
                OutputLabel     = snapshot.TotalOutputTokens     > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalOutputTokens)     : "—";
                CacheReadLabel  = snapshot.TotalCacheReadTokens  > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalCacheReadTokens)  : "—";
                CacheWriteLabel = snapshot.TotalCacheWriteTokens > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalCacheWriteTokens) : "—";

                IsUsageEmpty = !snapshot.HasData;

                _history.RecordToday(UsageProviderKind.Codex, null,
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

    public void UpdateActiveState(bool isEnabled, bool hideInactive)
    {
        IsActive = isEnabled && (!hideInactive || Percent > 0 || HasError);
    }

    public void UpdateResetLabels(bool showAbsoluteResetTime)
    {
        var now = DateTimeOffset.Now;
        if (_rawShortResetAt.HasValue && (_rawShortResetAt.Value - now).TotalMinutes < 10)
            Reset = UsageCalculator.FormatResetLabel(_rawShortResetAt, _rawShortResetEstimated, showAbsoluteResetTime, now);
        if (_rawLongResetAt.HasValue && (_rawLongResetAt.Value - now).TotalMinutes < 10)
            LongReset = UsageCalculator.FormatResetLabel(_rawLongResetAt, false, showAbsoluteResetTime, now);
    }
}
