using System.Collections.Generic;
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
                ResetAt = m.ResetTime,
                Window = ResolveWindowLength(m),
            };
            row.UpdateTimeProgress(now);
            rows.Add(row);
        }
        rows.Sort((a, b) => b.UsagePercent.CompareTo(a.UsagePercent));
        Models = rows;

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

    /// <summary>창 길이. 모르면 null — 시간선 마커를 그리지 않는다.</summary>
    public TimeSpan? Window { get; init; }

    [ObservableProperty] private string _resetAtLabel = "";

    /// <summary>
    /// 게이지 툴팁 — 절대 시각까지 적는다.
    /// 이름이 긴 행이라(그룹 × 창) 한 줄 라벨에 절대 시각까지 넣으면 이름이 잘린다.
    /// 다른 provider 의 설정(ShowAbsoluteResetTime)과 달리 여기서는 툴팁 자리를 쓴다.
    /// </summary>
    [ObservableProperty] private string _resetTooltip = "";

    [ObservableProperty] private bool _hasTimeline = false;
    [ObservableProperty] private double _timePercent = 0.0;

    internal void UpdateTimeProgress(DateTimeOffset now)
    {
        ResetAtLabel = UsageCalculator.FormatResetLabel(ResetAt, false, false, now);
        ResetTooltip = UsageCalculator.FormatResetLabel(ResetAt, false, true, now);
        var progress = UsageCalculator.TimeProgress(ResetAt, Window ?? TimeSpan.Zero, now);
        HasTimeline = progress.HasValue;
        TimePercent = progress ?? 0;
    }
}
