using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

/// <summary>
/// 100milionseek(DeepSeek 무료 API) 일일 한도 섹션(#306·#311). OpenCode 에서 쓰는 API 지만 집계 대상은 그 API 계정이라
/// OpenCode(로그인·Go 할당량)와 섞지 않고 독립 섹션으로 둔다. 키가 없으면 그릴 것이 없어 섹션이 숨는다.
/// </summary>
public sealed partial class HmsViewModel : ObservableObject
{
    private readonly HmsUsageService? _service;
    /// <summary>마지막으로 읽은 일일 한도. 확인이 잠시 실패해도 그날 창이 끝나기 전까지는 이 값을 보여 준다.</summary>
    private HmsDailyUsage? _last;
    /// <summary><see cref="_last"/> 를 읽은 키의 지문. 키를 바꾼 뒤에는 옛 키의 값을 이어 보여 주지 않는다.</summary>
    private string? _lastKeyId;
    /// <summary>안내를 고른 상태 — 언어를 바꿀 때 같은 안내를 새 언어로 다시 만든다.</summary>
    private HmsReadState _noticeState = HmsReadState.NoKey;

    /// <summary>일일 게이지 — 값이 있으면 한 줄, 없으면 비어 있다.</summary>
    [ObservableProperty] private IReadOnlyList<QuotaGaugeRow> _rows = [];
    /// <summary>키 거절·확인 실패 안내. 정상이거나 키가 없으면 빈 문자열.</summary>
    [ObservableProperty] private string _notice = "";
    /// <summary>"초기화 절대 시간 표시" — 다른 공급자와 같은 값을 MainViewModel 이 밀어 넣는다.</summary>
    [ObservableProperty] private bool _showAbsoluteResetTime;

    public HmsViewModel(HmsUsageService? service = null)
    {
        _service = service;
    }

    public bool HasQuota => Rows.Count > 0;

    /// <summary>마지막 조회 결과 — 휴대폰 피드에 실을 때 쓴다(#318). 아직 조회 전이면 null.</summary>
    internal HmsReadResult? LastResult { get; private set; }

    /// <summary>
    /// 휴대폰에 보낼 일일 한도. 이번 조회가 성공했을 때만 만든다 — 실패면 null 을 보내, 피드가 같은 키로 읽은 직전 값을
    /// 원래 관측 시각 그대로 이어 붙이게 한다(새 시각을 찍으면 낡은 값이 방금 값처럼 보인다).
    /// 하루 창이라 short 창 하나(1440분)로 보낸다 — 휴대폰은 이름 없이 창 길이로 "1d" 라고 적는다.
    /// </summary>
    internal static Models.UsageSyncQuotaSnapshot? CreateMobileQuota(HmsReadResult? result, DateTimeOffset now) =>
        result is { State: HmsReadState.Ok, Usage: { } usage } && usage.ResetAt > now
            ? new Models.UsageSyncQuotaSnapshot
            {
                HasData = true,
                ObservedAtUtc = now.ToUniversalTime(),
                ShortUsagePercent = usage.UsagePercent,
                ShortResetAt = usage.ResetAt,
                ShortWindowMinutes = 24 * 60,
                HasLongWindow = false,
            }
            : null;

    /// <summary>게이지나 안내 중 하나라도 그릴 것이 있는가 — 섹션을 보이는 근거다.</summary>
    public bool HasContent => HasQuota || Notice.Length > 0;

    /// <summary>접힌 줄 오른쪽 숫자. 값이 없고 안내만 있으면 "—".</summary>
    public string PercentLabel => Rows.Count > 0 ? Rows[0].UsagePercent.ToString("P0") : "—";

    public double Percent => Rows.Count > 0 ? Rows[0].UsagePercent : 0;

    public string SourceNote => Loc.HmsSourceNote;

    partial void OnRowsChanged(IReadOnlyList<QuotaGaugeRow> value)
    {
        OnPropertyChanged(nameof(HasQuota));
        OnPropertyChanged(nameof(HasContent));
        OnPropertyChanged(nameof(PercentLabel));
        OnPropertyChanged(nameof(Percent));
    }

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasContent));

    partial void OnShowAbsoluteResetTimeChanged(bool value)
    {
        foreach (var row in Rows)
            row.UpdateTimeProgress(DateTimeOffset.Now, value);
    }

    /// <summary>사이트를 읽는다. HTTP 만 쓰므로 UI 스레드를 붙잡지 않는다.</summary>
    public async Task RefreshAsync()
    {
        if (_service == null) return;
        var result = await _service.ReadAsync();
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => Apply(result, DateTimeOffset.Now));
    }

    /// <summary>
    /// 읽은 결과를 게이지·안내로 옮긴다. 키가 없으면 모두 비우고, 거절되면 값을 버리고 안내만,
    /// 잠시 실패하면 같은 키로 읽은 그날 값을 안내와 함께 유지한다.
    /// </summary>
    internal void Apply(HmsReadResult result, DateTimeOffset now)
    {
        LastResult = result;
        switch (result.State)
        {
            case HmsReadState.Ok when result.Usage != null:
                _last = result.Usage;
                _lastKeyId = result.KeyId;
                _noticeState = HmsReadState.Ok;
                break;
            case HmsReadState.InvalidKey:
                _last = null;
                _noticeState = HmsReadState.InvalidKey;
                break;
            case HmsReadState.NoKey:
                _last = null;
                _noticeState = HmsReadState.NoKey;
                break;
            default:
                _noticeState = HmsReadState.Unavailable;
                // 키를 바꾼 직후 실패했다면 남은 값은 옛 키의 것이다 — 새 키의 값처럼 보이지 않게 버린다.
                if (!string.Equals(result.KeyId, _lastKeyId, StringComparison.Ordinal)) _last = null;
                break;
        }
        Notice = NoticeFor(_noticeState);
        if (_last is { } usage && usage.ResetAt <= now) _last = null;
        Rows = _last is { } current ? [BuildRow(current, now, ShowAbsoluteResetTime)] : [];
    }

    internal static QuotaGaugeRow BuildRow(HmsDailyUsage usage, DateTimeOffset now, bool showAbsoluteResetTime)
    {
        var summary = Loc.HmsSummary(
            UsageCalculator.FormatTokenShort(usage.Used), UsageCalculator.FormatTokenShort(usage.Limit), usage.Requests);
        if (usage.BonusEndsAt is { } bonusEnds && bonusEnds > now)
            summary += " · " + Loc.HmsBonusEnds(UsageCalculator.FormatClockStamp(bonusEnds, now));
        var row = new QuotaGaugeRow
        {
            Key = "hms:daily",
            DisplayName = Loc.HmsDailyLabel,
            UsagePercent = usage.UsagePercent,
            Summary = summary,
            ResetAt = usage.ResetAt,
            // 하루 한도는 UTC 자정마다 새로 시작한다.
            Window = TimeSpan.FromDays(1),
        };
        row.UpdateTimeProgress(now, showAbsoluteResetTime);
        return row;
    }

    /// <summary>매초 — 리셋 카운트다운·시간선을 움직이고, UTC 자정이 지나면 어제 값을 내린다.</summary>
    public void UpdateTimeProgress(DateTimeOffset now)
    {
        if (_last is { } usage && usage.ResetAt <= now)
        {
            _last = null;
            Rows = [];
        }
        foreach (var row in Rows)
            row.UpdateTimeProgress(now, ShowAbsoluteResetTime);
    }

    private static string NoticeFor(HmsReadState state) => state switch
    {
        HmsReadState.InvalidKey => Loc.HmsInvalidKey,
        HmsReadState.Unavailable => Loc.HmsUnavailable,
        _ => "",
    };

    /// <summary>언어가 바뀌면 행 이름·요약·안내를 새 언어로 다시 만든다.</summary>
    public void RefreshLanguage()
    {
        Notice = NoticeFor(_noticeState);
        if (_last is { } usage) Rows = [BuildRow(usage, DateTimeOffset.Now, ShowAbsoluteResetTime)];
        OnPropertyChanged(nameof(SourceNote));
    }
}
