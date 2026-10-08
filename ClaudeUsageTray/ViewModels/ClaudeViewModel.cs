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
    // Has*Timeline 이 false 면 지금이 창 밖이라 위치를 모른다는 뜻 — 마커를 아예 숨긴다.
    [ObservableProperty] private double _shortTimePercent = 0;
    [ObservableProperty] private bool _hasShortTimeline = false;
    [ObservableProperty] private double _shortUsageCapped = 0;
    [ObservableProperty] private double _longTimePercent = 0;
    [ObservableProperty] private bool _hasLongTimeline = false;
    [ObservableProperty] private double _longUsageCapped = 0;
    [ObservableProperty] private string _shortPaceTip = "";
    [ObservableProperty] private string _longPaceTip = "";
    [ObservableProperty] private string _shortSummary = "";
    [ObservableProperty] private string _longSummary = "";
    [ObservableProperty] private string _shortDepletion = "";
    [ObservableProperty] private string _longDepletion = "";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowLoginAction))] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";

    // 재로그인만이 해결책인 에러(refresh 토큰 거절·토큰 없음)인지 — 팝업에 "터미널에서 로그인" 버튼을 띄운다(#180).
    // HasError 와 함께 봐서, 에러를 지우는 경로가 이 값을 따로 내리지 않아도 버튼이 남지 않게 한다.
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowLoginAction))] private bool _needsLogin = false;
    [ObservableProperty] private string _loginLaunchError = "";
    public bool ShowLoginAction => HasError && NeedsLogin;

    // claude CLI 가 이 PC 에 있는지 — 없으면 버튼이 "설치 후 로그인" 으로 바뀌어, 누르기 전에 설치가 진행된다는 걸 알린다.
    // 실제 분기는 클릭 시점에 다시 찾으므로 이 값은 문구용 힌트다. 모르는 동안은 설치돼 있다고 본다.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoginActionLabel))]
    [NotifyPropertyChangedFor(nameof(LoginActionTooltip))]
    private bool _isCliInstalled = true;

    public string LoginActionLabel   => IsCliInstalled ? Loc.ClaudeLoginInTerminal : Loc.ClaudeInstallAndLogin;
    public string LoginActionTooltip => IsCliInstalled ? Loc.ClaudeLoginInTerminalTooltip : Loc.ClaudeInstallAndLoginTooltip;

    public void RefreshLocalizedLabels()
    {
        OnPropertyChanged(nameof(LoginActionLabel));
        OnPropertyChanged(nameof(LoginActionTooltip));
        OnPropertyChanged(nameof(ShortPercentLabel));
        OnPropertyChanged(nameof(LongPercentLabel));
        OnPropertyChanged(nameof(HistoryChartTitle));
    }

    // 에러가 풀리면 지난 실행 실패 문구도 함께 지운다 — 다음에 다시 로그인이 필요해질 때 옛 실패가 보이지 않게.
    partial void OnHasErrorChanged(bool value)
    {
        if (!value) LoginLaunchError = "";
    }
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
