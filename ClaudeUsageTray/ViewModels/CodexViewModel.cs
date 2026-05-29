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
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var newPercent = snapshot.ShortUsagePercent;
                _rawShortResetAt = snapshot.ShortResetAt;
                _rawShortResetEstimated = snapshot.IsShortResetEstimated;
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
                IsLongVisible = snapshot.LongUsagePercent > 0 || snapshot.LongResetAt is not null;

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
