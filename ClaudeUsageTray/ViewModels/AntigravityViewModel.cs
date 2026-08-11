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
        HasData = true;
        HasError = false;
        ErrorMessage = "";
        TierName = tierName ?? "";
        PaidTierName = paidTierName ?? "";

        double worstUsed = 0;
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
            rows.Add(new AntigravityModelRow
            {
                ModelId = m.ModelId,
                DisplayName = ResolveDisplayName(m),
                UsagePercent = used,
                UsageLabel = Loc.PercentUsed(used),
                ResetAtLabel = FormatResetLabel(m.ResetTime),
            });
        }
        rows.Sort((a, b) => b.UsagePercent.CompareTo(a.UsagePercent));
        Models = rows;

        // 창마다 한도가 따로 걸리므로 평균은 가장 급한 제약을 가린다 (주간 90% + 5시간 0% → 45%).
        // 트레이 게이지에는 가장 많이 쓴 창을 올린다.
        Percent = worstUsed;
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

    internal static string FormatResetLabel(DateTimeOffset? resetAt)
    {
        if (resetAt is null) return "";
        var diff = resetAt.Value - DateTimeOffset.Now;
        if (diff.TotalSeconds <= 0) return "";
        string time;
        if (diff.TotalMinutes < 10) time = $"{(int)diff.TotalMinutes}m {diff.Seconds:D2}s";
        else if (diff.TotalHours < 1) time = $"{(int)diff.TotalMinutes}m";
        else if (diff.TotalDays < 1) time = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        else time = $"{(int)diff.TotalDays}d {diff.Hours}h";
        return Loc.ResetsIn(time);
    }
}
