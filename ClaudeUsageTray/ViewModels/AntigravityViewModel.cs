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

    /// <summary>Google AI 크레딧 잔액(천 단위 구분). 크레딧 대상이 아닌 계정이면 빈 문자열이라 줄 자체가 접힌다.</summary>
    [ObservableProperty] private string _aiCreditsText = "";

    [ObservableProperty] private IReadOnlyList<QuotaGaugeRow> _models = System.Array.Empty<QuotaGaugeRow>();
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private double _percent = 0.0;

    /// <summary>설정 화면의 "초기화 절대 시간 표시" 토글 — Claude·Codex 와 같은 값을 MainViewModel 이 그대로 밀어 넣는다.</summary>
    [ObservableProperty] private bool _showAbsoluteResetTime = false;

    /// <summary>
    /// 설정의 "Claude·GPT 한도도 표시" 토글. 꺼져 있으면(기본) Claude·GPT 그룹(3p) 창을 행에서도,
    /// 트레이 대표값(<see cref="Percent"/>)에서도 뺀다 — 팝업과 트레이 숫자가 어긋나지 않게 한 곳에서 거른다.
    /// 다른 PC 에서 받은 값도 같은 경로(<see cref="ApplyQuota"/>)를 타므로 같이 걸러진다.
    /// </summary>
    [ObservableProperty] private bool _showThirdPartyQuota = false;

    /// <summary>마지막 조회 결과 원본 — MainViewModel 이 다중 PC 동기화에 쓴다.</summary>
    public AntigravitySnapshot LastSnapshot { get; private set; } = new();

    // 화면에 올린 마지막 입력. 언어·표시 설정이 바뀌면 이 값으로 행을 다시 만든다
    // (LastSnapshot 과 별개다 — 다른 PC 에서 받은 값을 표시 중일 수 있다).
    private IReadOnlyList<AntigravityModelQuota> _appliedQuota = System.Array.Empty<AntigravityModelQuota>();

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
                Models = System.Array.Empty<QuotaGaugeRow>();
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
                Models = System.Array.Empty<QuotaGaugeRow>();
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
                Models = System.Array.Empty<QuotaGaugeRow>();
                return;
            }

            ApplyQuota(snap.Models, snap.TierName, snap.PaidTierName, snap.AiCredits);
        });
    }

    /// <summary>
    /// 모델별 할당량을 화면 상태로 옮긴다. 로컬 조회 결과와, 다중 PC 동기화로 받은 다른 PC 의 결과가
    /// 같은 경로를 타도록 분리해 둔 것 — 표시 규칙(내부 모델 제외·정렬·평균)이 어긋나지 않게 한다.
    /// </summary>
    public void ApplyQuota(IReadOnlyList<AntigravityModelQuota> models, string? tierName, string? paidTierName,
                           long? aiCredits = null)
    {
        _appliedQuota = models;

        HasData = true;
        HasError = false;
        ErrorMessage = "";
        TierName = tierName ?? "";
        PaidTierName = paidTierName ?? "";
        // 0 도 보여준다 — 크레딧을 다 써서 한도 소진 뒤 이어 쓸 수 없다는 뜻이다(gemini-cli /stats 와 같은 규칙).
        AiCreditsText = aiCredits is long credits ? credits.ToString("N0", System.Globalization.CultureInfo.CurrentCulture) : "";

        RebuildRows();
    }

    /// <summary>
    /// 마지막으로 올린 할당량으로 행과 트레이 대표값만 다시 만든다. 오류·배지·크레딧 같은 상태는 건드리지 않는다 —
    /// 언어나 표시 설정이 바뀌었을 뿐인데 다른 PC 값 위에 살려 둔 로컬 오류까지 지워지면 안 된다.
    /// </summary>
    private void RebuildRows()
    {
        var models = _appliedQuota;
        double worstUsed = 0;
        double worstSharedUsed = 0;
        bool hasSharedPool = false;
        var now = DateTimeOffset.Now;
        var rows = new List<QuotaGaugeRow>(models.Count);
        foreach (var m in models)
        {
            if (m.ResetTime is null) continue;
            // 다중 PC 동기화 최후 폴백(최대 24시간 전 관측치)을 받는 경우, 그 사이 이 창만
            // 리셋됐을 수 있다 — remainingFraction 은 리셋 전 값이라 실제보다 적게 남은 것처럼
            // 보인다. 리셋 시각이 이미 지난 행은 다음 실제 조회 전까지 아예 숨긴다.
            if (m.ResetTime <= now) continue;
            if (m.ModelId.StartsWith("chat_", StringComparison.Ordinal) ||
                m.ModelId.StartsWith("tab_",  StringComparison.Ordinal))
                continue;
            if (!ShowThirdPartyQuota && IsThirdPartyBucket(m.ModelId))
                continue;

            double used = Math.Clamp(1.0 - m.RemainingFraction, 0.0, 1.0);
            if (used > worstUsed) worstUsed = used;
            if (IsSharedPoolBucket(m.ModelId))
            {
                hasSharedPool = true;
                if (used > worstSharedUsed) worstSharedUsed = used;
            }

            // 아직 쓰지 않은 창도 남겨 둔다. 보여 주기로 한 그룹의 주간·5시간 칸이
            // 항상 보여야 지금 무엇이 얼마나 남았는지 읽을 수 있다.
            var row = new QuotaGaugeRow
            {
                Key = m.ModelId,
                DisplayName = ResolveDisplayName(m),
                UsagePercent = used,
                Summary = Loc.UsageSummary(used),
                ResetAt = m.ResetTime,
                Window = ResolveWindowLength(m),
            };
            row.UpdateTimeProgress(now, ShowAbsoluteResetTime);
            rows.Add(row);
        }
        Models = SortLikeOtherProviders(rows, models);

        // 창마다 한도가 따로 걸리므로 평균은 가장 급한 제약을 가린다 (주간 90% + 5시간 0% → 45%).
        // 트레이 게이지에는 가장 많이 쓴 창을 올린다.
        // 모델 하나에만 걸린 한도(Gemini 4 Argon 등, #204)는 행으로만 보이고 트레이 값에는 넣지 않는다 —
        // 그 모델을 다 써도 같은 풀의 다른 모델은 계속 쓸 수 있고, Claude 모델 한정 한도(#187)·
        // Codex 추가 한도(#182)도 같은 규칙이다. 공유 풀 행이 하나도 없으면(구버전이 동기화로 올린
        // 모델별 값, 버킷 이름 규칙이 바뀐 응답) 트레이가 0% 로 거짓말하지 않도록 전체 행으로 계산한다.
        Percent = hasSharedPool ? worstSharedUsed : worstUsed;
    }

    /// <summary>
    /// 시간선 마커와 리셋 카운트다운을 현재 시각 기준으로 다시 계산한다.
    /// Claude·Codex·OpenCode 와 같은 1초 주기로 불려, 막대 위 마커가 같은 속도로 흐른다.
    /// </summary>
    public void UpdateTimeProgress(DateTimeOffset now)
    {
        foreach (var row in Models)
            row.UpdateTimeProgress(now, ShowAbsoluteResetTime);
    }

    /// <summary>토글 즉시 반영 — API 재호출 없이 현재 행을 그대로 다시 포맷한다.</summary>
    partial void OnShowAbsoluteResetTimeChanged(bool value) => UpdateTimeProgress(DateTimeOffset.Now);

    /// <summary>토글 즉시 반영 — API 재호출 없이 마지막 값으로 행과 대표값을 다시 만든다.</summary>
    partial void OnShowThirdPartyQuotaChanged(bool value)
    {
        if (HasData && _appliedQuota.Count > 0) RebuildRows();
    }

    /// <summary>
    /// Claude·GPT 그룹(Google 이 자사 모델과 구분해 third-party 라 부르는 3p)의 창인가.
    /// 버킷 식별자는 "그룹-창" 형식이므로 창을 뗀 그룹 키가 정확히 3p 인 것만 본다.
    /// </summary>
    internal static bool IsThirdPartyBucket(string modelId) =>
        string.Equals(GroupKey(modelId), "3p", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 여러 모델이 함께 쓰는 공유 풀의 창인가 — Gemini 모델 묶음(gemini)과 Claude·GPT 묶음(3p).
    /// 그 밖의 그룹(Gemini 4 Argon 의 gemini-argon-wtu 등, #204)은 모델 하나에만 걸린 한도다.
    /// 그룹 키를 정확히 비교한다 — 앞부분만 보면 gemini 로 시작하는 모델 한정 그룹이 공유 풀로 잡힌다.
    /// </summary>
    internal static bool IsSharedPoolBucket(string modelId)
    {
        var group = GroupKey(modelId);
        return string.Equals(group, "gemini", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(group, "3p", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 게이지 순서를 Claude·Codex 와 맞춘다 — 짧은 창(5시간)이 위, 긴 창(주간)이 아래.
    /// 사용량 내림차순으로 세우면 새로고침마다 순서가 뒤바뀌어 같은 자리를 눈으로 좇을 수 없고,
    /// 다른 provider 는 창 길이 순으로 고정돼 있어 같은 화면에서 읽는 방식이 달라진다.
    ///
    /// 공유 풀(Gemini · Claude·GPT)을 먼저, 모델 한정 한도(Gemini 4 Argon 등)를 그 아래에 둔다 —
    /// Antigravity 앱 화면과 같은 순서이고, Claude 의 모델 한정 한도(#187)도 기본 게이지 아래에 붙는다.
    /// 같은 종류 안에서는 서버가 준 그룹 순서를 지켜 같은 그룹의 창이 붙어 보이게 한다.
    /// 창 길이를 모르는 행(새 창 종류)은 그룹 안에서 맨 뒤로 — 자리를 지어내지 않는다.
    /// </summary>
    private static IReadOnlyList<QuotaGaugeRow> SortLikeOtherProviders(
        List<QuotaGaugeRow> rows, IReadOnlyList<AntigravityModelQuota> source)
    {
        var groupOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var quota in source)
        {
            var key = GroupKey(quota.ModelId);
            if (!groupOrder.ContainsKey(key))
                groupOrder[key] = groupOrder.Count;
        }

        return [.. rows
            .OrderBy(r => IsSharedPoolBucket(r.Key) ? 0 : 1)
            .ThenBy(r => groupOrder.TryGetValue(GroupKey(r.Key), out var i) ? i : int.MaxValue)
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
        RebuildRows();
    }

    /// <summary>
    /// 아는 버킷은 앱 언어로 부르고, 모르는 그룹은 서버의 그룹 이름 + 앱 언어의 창 이름("Gemini 4 Argon · 주간")으로,
    /// 그것도 안 되는 버킷만 서버가 준 영어 문구를 쓴다.
    /// 표시 이름을 함께 보내지 않던 버전이 동기화한 값은 식별자를 다듬어 쓴다.
    /// </summary>
    internal static string ResolveDisplayName(AntigravityModelQuota quota)
    {
        var localized = Loc.AntigravityBucketLabel(quota.ModelId, quota.TokenType, quota.GroupName);
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
