using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class CodexViewModel : ObservableObject
{
    private readonly CodexUsageMonitor _monitor;
    private readonly HistoryService _history;
    // 알림용 "이전 값" 은 표시용 Percent 와 분리한다 — 조회 실패 때의 0% 가 기준선을 무너뜨리지 않게 하기 위함.
    private readonly QuotaAlertBaseline _alertBaseline = new();
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
    [ObservableProperty] private string _planLabel = "";
    [ObservableProperty] private string _shortWindowLabel = Loc.ShortWindow;
    [ObservableProperty] private string _longWindowLabel = Loc.LongWindow;
    [ObservableProperty] private string _inputLabel = "—";
    [ObservableProperty] private string _outputLabel = "—";
    [ObservableProperty] private string _cacheReadLabel = "—";
    [ObservableProperty] private bool _isUsageEmpty = true;

    public double PrevPercent => _alertBaseline.Percent;
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

    // thresholds: 사용자가 설정에서 켠 임계값(%). Claude 와 같은 기준을 따르도록 호출자가 넘긴다.
    // showUsageAlert 의 마지막 인자: 서버가 준 창 리셋 시각(추정치면 null). 여러 PC 의 같은 알림을 가르는 데 쓴다(#175).
    public async Task RefreshAsync(bool showAbsoluteResetTime, string ntfyTopic, bool notificationsEnabled, bool notifyOnQuotaReset, IReadOnlyCollection<int> thresholds, Action<int, string, string, string, DateTimeOffset?> showUsageAlert, Action showQuotaResetAlert)
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

                // 알림이 꺼져 있어도 기준선은 갱신한다 — 나중에 켰을 때 낡은 기준선과 비교하지 않도록.
                var alerts = _alertBaseline.Observe(
                    newPercent, snapshot.ShortResetAt, snapshot.IsShortResetEstimated,
                    DateTimeOffset.Now, thresholds, notifyOnQuotaReset);

                if (notificationsEnabled)
                {
                    if (alerts.QuotaReset)
                        showQuotaResetAlert();

                    // 추정 리셋은 PC 마다 달라(로그의 첫 활동 시각 기준) 같은 창을 가르는 근거가 못 된다.
                    var windowResetAt = snapshot.IsShortResetEstimated ? null : snapshot.ShortResetAt;
                    foreach (var t in alerts.CrossedThresholds)
                        showUsageAlert(t, Loc.Usage, Reset, ntfyTopic, windowResetAt);
                }

                Percent = newPercent;

                LongPercent   = snapshot.LongUsagePercent;
                _rawLongResetAt = snapshot.LongResetAt;
                LongReset     = UsageCalculator.FormatResetLabel(_rawLongResetAt, false, showAbsoluteResetTime, DateTimeOffset.Now);
                LongSummary   = Loc.UsageSummary(snapshot.LongUsagePercent);
                // 창 라벨은 window_minutes 로 결정(주간/5시간 등). 창 길이를 모르면 폴백.
                ShortWindowLabel = Loc.CodexWindowLabel(snapshot.ShortWindowMinutes);
                LongWindowLabel  = Loc.CodexWindowLabel(snapshot.LongWindowMinutes);
                // 장기(둘째) 창은 실제로 두 번째 창이 존재할 때만 노출.
                IsLongVisible = snapshot.LongWindowMinutes is not null || snapshot.LongUsagePercent > 0 || snapshot.LongResetAt is not null;

                // 요금제를 모르면 "ChatGPT plan" 같은 빈 껍데기 대신 배지를 숨긴다 — 다른 공급자와 같은 규칙.
                PlanLabel = PlanLabels.Codex(snapshot.PlanType);

                InputLabel      = snapshot.TotalInputTokens      > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalInputTokens)      : "—";
                OutputLabel     = snapshot.TotalOutputTokens     > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalOutputTokens)     : "—";
                CacheReadLabel  = snapshot.TotalCacheReadTokens  > 0 ? UsageCalculator.FormatTokenShort(snapshot.TotalCacheReadTokens)  : "—";

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

    public void UpdateResetLabels(bool showAbsoluteResetTime)
    {
        var now = DateTimeOffset.Now;
        if (_rawShortResetAt.HasValue && (_rawShortResetAt.Value - now).TotalMinutes < 10)
            Reset = UsageCalculator.FormatResetLabel(_rawShortResetAt, _rawShortResetEstimated, showAbsoluteResetTime, now);
        if (_rawLongResetAt.HasValue && (_rawLongResetAt.Value - now).TotalMinutes < 10)
            LongReset = UsageCalculator.FormatResetLabel(_rawLongResetAt, false, showAbsoluteResetTime, now);
    }
}
