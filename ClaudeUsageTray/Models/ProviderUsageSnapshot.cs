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
    public string? DataSource { get; set; }

    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheWriteTokens { get; set; }

    public int SessionCount { get; set; }
    public int RequestCount { get; set; }

    public long[] HourlyTokens { get; set; } = new long[24];

    /// <summary>
    /// 기본 창(Short/Long) 말고 공급자가 따로 거는 한도의 창들 — 예: Codex 의 이미지 생성 한도(#182).
    /// 한도 하나가 창을 둘(5시간·주간) 가질 수 있어 창 단위로 펴서 담는다. 같은 한도의 창은 붙어 있고,
    /// 한도끼리는 서버가 준 순서, 한도 안에서는 짧은 창이 먼저다.
    /// </summary>
    public List<AdditionalQuotaWindow> AdditionalQuotas { get; set; } = [];

    public OpenCodeUsageDetails? OpenCodeDetails { get; set; }
}

/// <summary>추가 한도의 창 하나(계정 단위 값).</summary>
public sealed class AdditionalQuotaWindow
{
    /// <summary>한도 식별자 (Codex: API 의 <c>metered_feature</c>, 세션 로그의 <c>limit_id</c>).</summary>
    public string LimitId { get; init; } = "";

    /// <summary>서버가 준 한도 이름 (Codex: <c>limit_name</c>). 없으면 빈 문자열.</summary>
    public string LimitName { get; init; } = "";

    public double UsagePercent { get; init; }   // 0..1
    public DateTimeOffset? ResetAt { get; init; }
    public int? WindowMinutes { get; init; }

    /// <summary>
    /// 이 값을 관측한 시각 (Direct API: 조회 시각, 세션 로그: 그 줄의 기록 시각).
    /// 이 PC 로그와 다른 PC 동기화 값이 함께 있을 때 더 최근 것을 고르는 근거다 — 며칠 전 로그 값이
    /// 다른 PC 가 방금 본 값을 가리지 않게.
    /// </summary>
    public DateTimeOffset? ObservedAt { get; init; }
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
    /// <summary>
    /// 로그인은 돼 있지만 Go 할당량이 있는 작업공간이 없다(미구독·해지·일시정지·결제 대기).
    /// 확인 실패와 섞으면 "곧 다시 시도" 안내 뒤로 진짜 이유가 숨는다.
    /// </summary>
    NoQuota,
}

public readonly record struct OpenCodeWebReadResult(OpenCodeWebUsage? Usage, OpenCodeWebSessionState State)
{
    public static OpenCodeWebReadResult NotAttempted => new(null, OpenCodeWebSessionState.Unknown);
    public static OpenCodeWebReadResult SignedOut => new(null, OpenCodeWebSessionState.SignedOut);
    public static OpenCodeWebReadResult Unavailable => new(null, OpenCodeWebSessionState.Unavailable);
    public static OpenCodeWebReadResult NoQuota => new(null, OpenCodeWebSessionState.NoQuota);
    public static OpenCodeWebReadResult Success(OpenCodeWebUsage usage) =>
        new(usage, OpenCodeWebSessionState.Authenticated);
}

public class OpenCodePeriodUsage
{
    public long Tokens { get; set; }
    public int Requests { get; set; }
    public decimal CostUsd { get; set; }
}
