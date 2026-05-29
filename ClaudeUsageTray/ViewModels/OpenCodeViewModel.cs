using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class OpenCodeViewModel : ObservableObject
{
    private readonly OpenCodeUsageMonitor _monitor;
    private readonly HistoryService _history;
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
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private bool _isUsageEmpty = true;

    public int LastRequestCount => _lastRequestCount;
    public long LastInputTokens => _lastInputTokens;
    public long LastOutputTokens => _lastOutputTokens;

    public OpenCodeViewModel(OpenCodeUsageMonitor monitor, HistoryService history)
    {
        _monitor = monitor;
        _history = history;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var max = _history.GetRecentMaxTotalTokens(UsageProviderKind.OpenCode, null, 7);
            var goal = Math.Max(10000, max);
            var snapshot = _monitor.GetTodaySnapshot(goal);
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
                Percent = snapshot.ShortUsagePercent;
                IsUsageEmpty = !snapshot.HasData;

                _history.RecordToday(UsageProviderKind.OpenCode, null,
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
        IsActive = isEnabled && (!hideInactive || _lastRequestCount > 0 || HasError);
    }
}
