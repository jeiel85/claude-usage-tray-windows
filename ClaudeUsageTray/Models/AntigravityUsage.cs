using System;
using System.Collections.Generic;

namespace ClaudeUsageTray.Models;

/// <summary>
/// quota 버킷 하나.
/// Antigravity 의 retrieveUserQuotaSummary 응답에서 모델 그룹(Gemini / Claude·GPT)별로
/// 주간·5시간 창이 각각 이 형식으로 온다.
/// </summary>
public sealed class AntigravityModelQuota
{
    /// <summary>버킷 식별자. 예: "gemini-weekly", "3p-5h".</summary>
    public string ModelId { get; init; } = "";

    /// <summary>창 종류. Summary 응답에서는 "weekly" 또는 "5h".</summary>
    public string TokenType { get; init; } = "";

    public double RemainingFraction { get; init; }           // 0.0 ~ 1.0 (잔여 비율)
    public DateTimeOffset? ResetTime { get; init; }

    /// <summary>
    /// 서버가 내려준 표시 이름. 예: "Gemini Models · Weekly Limit".
    /// 비어 있으면 화면에서 <see cref="ModelId"/> 를 사람이 읽을 형태로 바꿔 쓴다
    /// (구버전이 동기화로 올린 모델별 값이 이 경우다).
    /// </summary>
    public string DisplayName { get; init; } = "";
}

/// <summary>
/// Antigravity 전체 스냅샷 — 모델 N개 + tier 정보.
/// MainViewModel 이 RefreshAntigravityInternalAsync 에서 채워서 UI 에 노출.
/// </summary>
public sealed class AntigravitySnapshot
{
    public bool HasData { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 조회하지 못한 이유가 "이 PC 에는 볼 것이 없다" 인 경우 true.
    /// Antigravity 미설치·미로그인·세션 만료가 여기 해당하며, 화면에는 빨간 오류 대신 섹션 숨김으로 나타난다.
    /// 실패 사유를 문구로 판별하면 문구를 고칠 때 조용히 깨지므로 스냅샷이 직접 들고 있는다.
    /// </summary>
    public bool IsInformational { get; init; }
    public string? TierName { get; init; }                   // 예: "Gemini Code Assist"
    public string? PaidTierName { get; init; }               // 예: "Gemini Code Assist in Google One AI Pro"
    public string? UserEmail { get; init; }
    public IReadOnlyList<AntigravityModelQuota> Models { get; init; } = Array.Empty<AntigravityModelQuota>();
}
