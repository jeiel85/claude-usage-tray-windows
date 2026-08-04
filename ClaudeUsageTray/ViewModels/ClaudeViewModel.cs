using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

// Claude 섹션의 표시 상태 홀더. 값은 전적으로 MainViewModel.RefreshClaudeAsync 가 채우고
// UsagePopup.xaml 이 바인딩한다. (자체 새로고침 로직은 MainViewModel 로 일원화되어 제거됨)
public partial class ClaudeViewModel : ObservableObject
{
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShortPercentLabel))] private double _shortPercent = 0;
    [ObservableProperty] private string _shortReset = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(LongPercentLabel))] private double _longPercent = 0;
    [ObservableProperty] private string _longReset = "";

    // 할당량을 한 번이라도 받아왔는지. false 면 ShortPercent/LongPercent 의 0 은 "사용 0%"가 아니라
    // "아직 모름"이므로, 0% 라고 단정해 보여주지 않는다.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShortPercentLabel))]
    [NotifyPropertyChangedFor(nameof(LongPercentLabel))]
    private bool _hasQuotaData = false;

    public string ShortPercentLabel => HasQuotaData ? ShortPercent.ToString("P0") : Loc.QuotaUnknownMark;
    public string LongPercentLabel  => HasQuotaData ? LongPercent.ToString("P0")  : Loc.QuotaUnknownMark;

    // 시간 진행률(윈도우 경과 비율, 0~1) — 사용량 막대와 비교해 페이스를 육안으로 드러낸다.
    // *UsageCapped = min(사용량, 시간) — 보라 레이어 폭. 사용량이 시간을 앞지른 만큼만 주황으로 노출된다.
    [ObservableProperty] private double _shortTimePercent = 0;
    [ObservableProperty] private double _shortUsageCapped = 0;
    [ObservableProperty] private double _longTimePercent = 0;
    [ObservableProperty] private double _longUsageCapped = 0;
    [ObservableProperty] private string _shortPaceTip = "";
    [ObservableProperty] private string _longPaceTip = "";
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

    public string HistoryChartTitle => Loc.HistoryTitleFor("Claude");
}
