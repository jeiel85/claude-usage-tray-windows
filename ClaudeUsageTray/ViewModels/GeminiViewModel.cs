using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class GeminiViewModel : ObservableObject
{
    private readonly GeminiCliUsageMonitor _monitor;
    private readonly HistoryService _history;
    private int _lastRequestCount = 0;
    private long _lastOutputTokens = 0;

    [ObservableProperty] private double _percent = 0;
    [ObservableProperty] private string _reset = "";
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _note = Loc.ProviderGeminiCliNote;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _requestsLabel = "";
    [ObservableProperty] private string _outputTokensLabel = "";
    [ObservableProperty] private string _inputLabel = "—";
    [ObservableProperty] private string _cacheReadLabel = "—";
    [ObservableProperty] private string _cacheWriteLabel = "—";
    [ObservableProperty] private bool _isActive = false;
    [ObservableProperty] private bool _isUsageEmpty = true;

    public int LastRequestCount => _lastRequestCount;
    public long LastOutputTokens => _lastOutputTokens;

    public GeminiViewModel(GeminiCliUsageMonitor monitor, HistoryService history)
    {
        _monitor = monitor;
        _history = history;
    }

    public async Task RefreshAsync()
    {
        try
        {
            var snapshot = _monitor.GetTodaySnapshot();
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var informational = UsageCalculator.IsNoUsageInformational(snapshot.ErrorMessage, UsageProviderKind.GeminiCli);
                HasError = !snapshot.HasData && !string.IsNullOrWhiteSpace(snapshot.ErrorMessage) && !informational;
                ErrorMessage = informational ? "" : (snapshot.ErrorMessage ?? "");
                
                _lastRequestCount = snapshot.RequestCount;
                _lastOutputTokens = snapshot.TotalOutputTokens;

                RequestsLabel = snapshot.RequestCount > 0
                    ? Loc.CurrentLang == "ko" ? $"{snapshot.RequestCount}회" : $"{snapshot.RequestCount} req"
                    : "—";
                InputLabel = snapshot.TotalInputTokens > 0
                    ? UsageCalculator.FormatTokenShort(snapshot.TotalInputTokens)
                    : "—";
                OutputTokensLabel = snapshot.TotalOutputTokens > 0
                    ? UsageCalculator.FormatTokenShort(snapshot.TotalOutputTokens)
                    : "—";
                CacheReadLabel = snapshot.TotalCacheReadTokens > 0
                    ? UsageCalculator.FormatTokenShort(snapshot.TotalCacheReadTokens)
                    : "—";
                CacheWriteLabel = "—";
                Summary = snapshot.HasData
                    ? Loc.GeminiCliRequestSummary(snapshot.RequestCount, snapshot.TotalOutputTokens)
                    : snapshot.ErrorMessage ?? "";

                var max = _history.GetRecentMaxTotalTokens(UsageProviderKind.GeminiCli, null, 7);
                var goal = Math.Max(10000, max);
                var percent = Math.Clamp(snapshot.TotalOutputTokens / (double)goal, 0, 1);
                Percent = percent;

                IsUsageEmpty = !snapshot.HasData;

                _history.RecordToday(UsageProviderKind.GeminiCli, null,
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
