using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

/// <summary>
/// 할당량 게이지 한 칸(한도 × 창)의 화면 상태 — Antigravity 의 모델 그룹별 창, Codex 의 추가 한도(#182),
/// Claude 의 모델 한정 한도(#187)가 함께 쓴다.
/// 퍼센트·이름은 조회할 때 정해지지만 리셋 카운트다운과 시간선 마커는 매초 움직이므로,
/// 행을 통째로 다시 만들지 않고 <see cref="UpdateTimeProgress"/> 로 제자리에서 갱신한다.
/// </summary>
public sealed partial class QuotaGaugeRow : ObservableObject
{
    /// <summary>행이 속한 한도의 식별자 (Antigravity: 버킷 ID "gemini-weekly", Codex: 한도 ID, Claude: "weekly:fable").</summary>
    public string Key { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public double UsagePercent { get; init; }        // 0..1
    public DateTimeOffset? ResetAt { get; init; }

    /// <summary>게이지 아래 한 줄 요약 ("70% 사용 · 잔량 30%") — Claude·Codex 게이지와 같은 자리.</summary>
    public string Summary { get; init; } = "";

    /// <summary>창 길이. 모르면 null — 시간선 마커를 그리지 않는다.</summary>
    public TimeSpan? Window { get; init; }

    [ObservableProperty] private string _resetAtLabel = "";

    /// <summary>게이지 툴팁 — Claude·Codex 게이지 툴팁과 같은 페이스 문구 한 줄.</summary>
    [ObservableProperty] private string _paceTip = "";

    [ObservableProperty] private bool _hasTimeline = false;
    [ObservableProperty] private double _timePercent = 0.0;

    /// <summary>
    /// 추가 한도 창 목록을 게이지 행으로 만든다 — Codex 추가 한도(#182)와 Claude 모델 한정 한도(#187)가 같은 규칙을 탄다.
    /// 행 이름은 "한도 이름 · 창"("이미지 생성 · 주간", "Fable · 주간")이고, 한도 이름을 부르는 법만 공급자가 정한다.
    /// 리셋 시각이 이미 지난 창은 지금 창을 설명하지 않으므로 뺀다.
    /// </summary>
    internal static IReadOnlyList<QuotaGaugeRow> BuildRows(
        IEnumerable<AdditionalQuotaWindow> windows, Func<AdditionalQuotaWindow, string> limitName,
        DateTimeOffset now, bool showAbsoluteResetTime)
    {
        var rows = new List<QuotaGaugeRow>();
        foreach (var window in windows)
        {
            if (window.ResetAt is { } reset && reset <= now) continue;

            var row = new QuotaGaugeRow
            {
                Key = window.LimitId,
                DisplayName = Loc.AdditionalQuotaLabel(limitName(window), window.WindowMinutes),
                UsagePercent = window.UsagePercent,
                Summary = Loc.UsageSummary(window.UsagePercent),
                ResetAt = window.ResetAt,
                Window = window.WindowMinutes is int minutes && minutes > 0 ? TimeSpan.FromMinutes(minutes) : null,
            };
            row.UpdateTimeProgress(now, showAbsoluteResetTime);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>
    /// "초기화 절대 시간 표시" 설정을 Claude·Codex·OpenCode 와 같은 방식으로 라벨에 반영한다.
    /// 행 이름("그룹 · 창")이 길어 절대 시각과 함께라면 잘릴 수 있지만, 이름 TextBlock 이
    /// 이미 CharacterEllipsis + 전체 이름 툴팁으로 그 경우를 감당하도록 되어 있다(#139).
    /// </summary>
    internal void UpdateTimeProgress(DateTimeOffset now, bool showAbsoluteResetTime)
    {
        ResetAtLabel = UsageCalculator.FormatResetLabel(ResetAt, false, showAbsoluteResetTime, now);

        var window = Window ?? TimeSpan.Zero;
        var progress = UsageCalculator.TimeProgress(ResetAt, window, now);
        HasTimeline = progress.HasValue;
        TimePercent = progress ?? 0;
        // 창 초반에는 페이스 판정을 유보한다 — 하한은 Codex 와 같은 창 길이의 1/60(5시간→5분, 주간→2.8시간).
        PaceTip = Loc.PaceTip(progress, UsagePercent,
            UsageCalculator.IsPaceSettled(progress, window, window / 60));
    }
}
