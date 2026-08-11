using System;

namespace ClaudeUsageTray.Models;

public class ProviderUsageSnapshot
{
    public bool HasData { get; set; }
    public bool IsLimited { get; set; }
    public string? ErrorMessage { get; set; }

    public double ShortUsagePercent { get; set; }
    public DateTimeOffset? ShortResetAt { get; set; }
    public bool IsShortResetEstimated { get; set; }
    public int? ShortWindowMinutes { get; set; }

    public double LongUsagePercent { get; set; }
    public DateTimeOffset? LongResetAt { get; set; }
    public int? LongWindowMinutes { get; set; }

    public string? PlanType { get; set; } // e.g. "Pro", "Plus", "Free"
    public bool IsSubscriptionActive { get; set; } = true;
    public string? DataSource { get; set; }

    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheWriteTokens { get; set; }

    public int SessionCount { get; set; }
    public int RequestCount { get; set; }

    public long[] HourlyTokens { get; set; } = new long[24];

    public OpenCodeUsageDetails? OpenCodeDetails { get; set; }
}

public class OpenCodeUsageDetails
{
    public OpenCodePeriodUsage LastFiveHours { get; set; } = new();
    public OpenCodePeriodUsage LastSevenDays { get; set; } = new();
    public OpenCodePeriodUsage ThisMonth { get; set; } = new();
    public string? LimitKind { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public OpenCodeWebUsage? WebUsage { get; set; }
}

public sealed class OpenCodeWebUsage
{
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public OpenCodeQuotaWindow Rolling { get; init; } = new();
    public OpenCodeQuotaWindow Weekly { get; init; } = new();
    public OpenCodeQuotaWindow Monthly { get; init; } = new();
}

public sealed class OpenCodeQuotaWindow
{
    public double UsagePercent { get; init; }
    public DateTimeOffset ResetAt { get; init; }
}

/// <summary>
/// 공식 할당량을 읽어보고 알게 된 웹 세션 상태.
/// "못 읽었다" 를 한 덩어리로 묶으면 로그아웃과 네트워크 실패를 구분할 수 없어,
/// 부팅 직후처럼 네트워크가 늦게 올라오는 상황에서 멀쩡한 세션에 로그인 버튼을 띄우게 된다.
/// </summary>
public enum OpenCodeWebSessionState
{
    /// <summary>확인을 시도하지 않았다(웹 조회 비활성 등).</summary>
    Unknown,
    /// <summary>공식 값을 읽어냈다.</summary>
    Authenticated,
    /// <summary>서버가 로그인 페이지로 되돌려보냈다 — 세션이 실제로 만료됐다.</summary>
    SignedOut,
    /// <summary>탐색 실패·타임아웃 등으로 로그인 여부조차 확인하지 못했다.</summary>
    Unavailable,
}

public readonly record struct OpenCodeWebReadResult(OpenCodeWebUsage? Usage, OpenCodeWebSessionState State)
{
    public static OpenCodeWebReadResult NotAttempted => new(null, OpenCodeWebSessionState.Unknown);
    public static OpenCodeWebReadResult SignedOut => new(null, OpenCodeWebSessionState.SignedOut);
    public static OpenCodeWebReadResult Unavailable => new(null, OpenCodeWebSessionState.Unavailable);
    public static OpenCodeWebReadResult Success(OpenCodeWebUsage usage) =>
        new(usage, OpenCodeWebSessionState.Authenticated);
}

public class OpenCodePeriodUsage
{
    public long Tokens { get; set; }
    public int Requests { get; set; }
    public decimal CostUsd { get; set; }
}
