using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

public partial class AntigravityViewModel : ObservableObject
{
    private readonly AntigravityUsageMonitor _monitor;

    [ObservableProperty] private bool _hasData = false;
    [ObservableProperty] private bool _hasError = false;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private string _tierName = "";
    [ObservableProperty] private string _paidTierName = "";
    [ObservableProperty] private IReadOnlyList<AntigravityModelRow> _models = System.Array.Empty<AntigravityModelRow>();
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private double _percent = 0.0;

    /// <summary>마지막 조회 결과 원본 — MainViewModel 이 다중 PC 동기화에 쓴다.</summary>
    public AntigravitySnapshot LastSnapshot { get; private set; } = new();

    // 화면에 올린 마지막 입력. 언어가 바뀌면 이 값으로 행을 다시 만든다
    // (LastSnapshot 과 별개다 — 다른 PC 에서 받은 값을 표시 중일 수 있다).
    private IReadOnlyList<AntigravityModelQuota> _appliedQuota = System.Array.Empty<AntigravityModelQuota>();
    private string? _appliedTierName;
    private string? _appliedPaidTierName;

    public AntigravityViewModel(AntigravityUsageMonitor monitor)
    {
        _monitor = monitor;
    }

    public async Task RefreshAsync()
    {
        if (!IsEnabled)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasData = false;
                HasError = false;
                ErrorMessage = "";
                Models = System.Array.Empty<AntigravityModelRow>();
            });
            return;
        }

        AntigravitySnapshot snap;
        try
        {
            snap = await _monitor.GetSnapshotAsync();
        }
        catch (Exception ex)
        {
            LastSnapshot = new AntigravitySnapshot { ErrorMessage = ex.Message };
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                HasData = false;
                HasError = true;
                ErrorMessage = ex.Message;
                Models = System.Array.Empty<AntigravityModelRow>();
            });
            return;
        }

        LastSnapshot = snap;

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (!snap.HasData)
            {
                HasData = false;
                HasError = !snap.IsInformational && !string.IsNullOrEmpty(snap.ErrorMessage);
                ErrorMessage = HasError ? (snap.ErrorMessage ?? "") : "";
                Models = System.Array.Empty<AntigravityModelRow>();
                return;
            }

            ApplyQuota(snap.Models, snap.TierName, snap.PaidTierName);
        });
    }

    /// <summary>
    /// 모델별 할당량을 화면 상태로 옮긴다. 로컬 조회 결과와, 다중 PC 동기화로 받은 다른 PC 의 결과가
    /// 같은 경로를 타도록 분리해 둔 것 — 표시 규칙(내부 모델 제외·정렬·평균)이 어긋나지 않게 한다.
    /// </summary>
    public void ApplyQuota(IReadOnlyList<AntigravityModelQuota> models, string? tierName, string? paidTierName)
    {
        _appliedQuota = models;
        _appliedTierName = tierName;
        _appliedPaidTierName = paidTierName;

        HasData = true;
        HasError = false;
        ErrorMessage = "";
        TierName = tierName ?? "";
        PaidTierName = paidTierName ?? "";

        double worstUsed = 0;
        var now = DateTimeOffset.Now;
        var rows = new List<AntigravityModelRow>(models.Count);
        foreach (var m in models)
        {
            if (m.ResetTime is null) continue;
            if (m.ModelId.StartsWith("chat_", StringComparison.Ordinal) ||
                m.ModelId.StartsWith("tab_",  StringComparison.Ordinal))
                continue;

            double used = Math.Clamp(1.0 - m.RemainingFraction, 0.0, 1.0);
            if (used > worstUsed) worstUsed = used;

            // 아직 쓰지 않은 창도 남겨 둔다. Antigravity 화면과 같은 네 칸(그룹 × 주간·5시간)이
            // 항상 보여야 지금 무엇이 얼마나 남았는지 읽을 수 있다.
            var row = new AntigravityModelRow
            {
                ModelId = m.ModelId,
                DisplayName = ResolveDisplayName(m),
                UsagePercent = used,
                Summary = Loc.UsageSummary(used),
                ResetAt = m.ResetTime,
                Window = ResolveWindowLength(m),
            };
            row.UpdateTimeProgress(now);
            rows.Add(row);
        }
        Models = SortLikeOtherProviders(rows, models);

        // 창마다 한도가 따로 걸리므로 평균은 가장 급한 제약을 가린다 (주간 90% + 5시간 0% → 45%).
        // 트레이 게이지에는 가장 많이 쓴 창을 올린다.
        Percent = worstUsed;
    }

    /// <summary>
    /// 시간선 마커와 리셋 카운트다운을 현재 시각 기준으로 다시 계산한다.
    /// Claude·Codex·OpenCode 와 같은 1초 주기로 불려, 막대 위 마커가 같은 속도로 흐른다.
    /// </summary>
    public void UpdateTimeProgress(DateTimeOffset now)
    {
        foreach (var row in Models)
            row.UpdateTimeProgress(now);
    }

    /// <summary>
    /// 게이지 순서를 Claude·Codex 와 맞춘다 — 짧은 창(5시간)이 위, 긴 창(주간)이 아래.
    /// 사용량 내림차순으로 세우면 새로고침마다 순서가 뒤바뀌어 같은 자리를 눈으로 좇을 수 없고,
    /// 다른 provider 는 창 길이 순으로 고정돼 있어 같은 화면에서 읽는 방식이 달라진다.
    ///
    /// 그룹(Gemini · Claude·GPT)끼리는 서버가 준 순서를 지켜 같은 그룹의 두 창이 붙어 보이게 한다.
    /// 창 길이를 모르는 행(새 창 종류)은 맨 뒤로 — 자리를 지어내지 않는다.
    /// </summary>
    private static IReadOnlyList<AntigravityModelRow> SortLikeOtherProviders(
        List<AntigravityModelRow> rows, IReadOnlyList<AntigravityModelQuota> source)
    {
        var groupOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var quota in source)
        {
            var key = GroupKey(quota.ModelId);
            if (!groupOrder.ContainsKey(key))
                groupOrder[key] = groupOrder.Count;
        }

        return [.. rows
            .OrderBy(r => groupOrder.TryGetValue(GroupKey(r.ModelId), out var i) ? i : int.MaxValue)
            .ThenBy(r => r.Window ?? TimeSpan.MaxValue)];
    }

    /// <summary>버킷 식별자에서 창 종류를 뗀 앞부분이 모델 그룹이다 ("gemini-weekly" → "gemini").</summary>
    private static string GroupKey(string modelId)
    {
        var dash = modelId.LastIndexOf('-');
        return dash > 0 ? modelId[..dash] : modelId;
    }

    /// <summary>
    /// 시간선 마커를 그리려면 창 길이가 필요하다 — 응답의 window 값("weekly"·"5h")이 곧 길이다.
    /// 표시 이름과 달리 창 종류를 함께 올리지 않던 버전이 동기화한 값은 비어 있을 수 있어,
    /// 버킷 식별자 꼬리("gemini-weekly" → weekly)로 한 번 더 본다.
    /// 어느 쪽으로도 모르면 null — 위치를 지어내지 않고 마커를 그리지 않는다.
    /// </summary>
    internal static TimeSpan? ResolveWindowLength(AntigravityModelQuota quota)
    {
        var window = string.IsNullOrWhiteSpace(quota.TokenType)
            ? BucketIdSuffix(quota.ModelId)
            : quota.TokenType;

        return window.ToLowerInvariant() switch
        {
            "weekly" => TimeSpan.FromDays(7),
            "5h"     => TimeSpan.FromHours(5),
            _        => null,
        };
    }

    private static string BucketIdSuffix(string modelId)
    {
        var dash = modelId.LastIndexOf('-');
        return dash >= 0 && dash < modelId.Length - 1 ? modelId[(dash + 1)..] : "";
    }

    /// <summary>
    /// 언어를 바꿨을 때 이미 만들어 둔 행을 다시 만든다.
    /// 행의 문구는 만들 때 한 번 정해지므로, 다시 만들지 않으면 다음 조회까지 이전 언어로 남는다.
    /// </summary>
    public void RefreshLocalizedLabels()
    {
        if (!HasData || _appliedQuota.Count == 0) return;
        ApplyQuota(_appliedQuota, _appliedTierName, _appliedPaidTierName);
    }

    /// <summary>
    /// 아는 버킷은 앱 언어로 부르고, 모르는 버킷만 서버가 준 영어 문구를 쓴다.
    /// 표시 이름을 함께 보내지 않던 버전이 동기화한 값은 식별자를 다듬어 쓴다.
    /// </summary>
    internal static string ResolveDisplayName(AntigravityModelQuota quota)
    {
        var localized = Loc.AntigravityBucketLabel(quota.ModelId, quota.TokenType);
        if (localized is not null) return localized;

        return string.IsNullOrWhiteSpace(quota.DisplayName)
            ? FormatModelName(quota.ModelId)
            : quota.DisplayName;
    }

    internal static string FormatModelName(string modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return "(unknown)";
        var parts = modelId.Split('-');
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            var p = parts[i];
            if (p.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(p[0]));
            if (p.Length > 1) sb.Append(p[1..]);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Antigravity 할당량 한 칸(그룹 × 창)의 화면 상태.
/// 퍼센트·이름은 조회할 때 정해지지만 리셋 카운트다운과 시간선 마커는 매초 움직이므로,
/// 행을 통째로 다시 만들지 않고 <see cref="UpdateTimeProgress"/> 로 제자리에서 갱신한다.
/// </summary>
public sealed partial class AntigravityModelRow : ObservableObject
{
    public string ModelId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public double UsagePercent { get; init; }        // 0..1
    public DateTimeOffset? ResetAt { get; init; }

    /// <summary>게이지 아래 한 줄 요약 ("70% 사용 · 잔량 30%") — Claude·Codex 게이지와 같은 자리.</summary>
    public string Summary { get; init; } = "";

    /// <summary>창 길이. 모르면 null — 시간선 마커를 그리지 않는다.</summary>
    public TimeSpan? Window { get; init; }

    [ObservableProperty] private string _resetAtLabel = "";

    /// <summary>
    /// 게이지 툴팁 — 첫 줄은 Claude·Codex 게이지 툴팁과 같은 페이스 문구,
    /// 둘째 줄은 리셋 절대 시각이다.
    ///
    /// 절대 시각을 다른 provider 처럼 한 줄 라벨에 붙이지 않는 이유: 이 행의 이름은 "그룹 · 창"이라
    /// 320px 팝업에서 절대 시각까지 넣으면 이름이 "Gemini 모..." 로 잘려 어느 창인지 구분되지 않는다.
    /// (다른 provider 의 행 이름은 "주간 윈도우" 한 덩어리라 같은 문제가 없다.)
    /// </summary>
    [ObservableProperty] private string _paceTip = "";

    [ObservableProperty] private bool _hasTimeline = false;
    [ObservableProperty] private double _timePercent = 0.0;

    internal void UpdateTimeProgress(DateTimeOffset now)
    {
        ResetAtLabel = UsageCalculator.FormatResetLabel(ResetAt, false, false, now);

        var window = Window ?? TimeSpan.Zero;
        var progress = UsageCalculator.TimeProgress(ResetAt, window, now);
        HasTimeline = progress.HasValue;
        TimePercent = progress ?? 0;
        // 창 초반에는 페이스 판정을 유보한다 — 하한은 Codex 와 같은 창 길이의 1/60(5시간→5분, 주간→2.8시간).
        var pace = Loc.PaceTip(progress, UsagePercent,
            UsageCalculator.IsPaceSettled(progress, window, window / 60));
        var absolute = UsageCalculator.FormatResetLabel(ResetAt, false, true, now);
        PaceTip = string.IsNullOrEmpty(absolute) ? pace : $"{pace}\n{absolute.TrimStart(' ', '·')}";
    }
}
