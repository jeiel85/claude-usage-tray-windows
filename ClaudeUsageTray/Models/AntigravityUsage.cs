using System;
using System.Collections.Generic;

namespace ClaudeUsageTray.Models;

/// <summary>
/// 단일 모델의 quota 버킷.
/// Antigravity 의 retrieveUserQuota 응답이 모델별로 이 형식으로 옴.
/// </summary>
public sealed class AntigravityModelQuota
{
    public string ModelId { get; init; } = "";
    public string TokenType { get; init; } = "";            // 보통 "REQUESTS"
    public double RemainingFraction { get; init; }           // 0.0 ~ 1.0 (잔여 비율)
    public DateTimeOffset? ResetTime { get; init; }
}

/// <summary>
/// Antigravity 전체 스냅샷 — 모델 N개 + tier 정보.
/// MainViewModel 이 RefreshAntigravityInternalAsync 에서 채워서 UI 에 노출.
/// </summary>
public sealed class AntigravitySnapshot
{
    public bool HasData { get; init; }
    public string? ErrorMessage { get; init; }
    public string? TierName { get; init; }                   // 예: "Gemini Code Assist"
    public string? PaidTierName { get; init; }               // 예: "Gemini Code Assist in Google One AI Pro"
    public string? UserEmail { get; init; }
    public IReadOnlyList<AntigravityModelQuota> Models { get; init; } = Array.Empty<AntigravityModelQuota>();
}
