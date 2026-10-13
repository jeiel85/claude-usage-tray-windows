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

    /// <summary>
    /// Codex 전용: Direct API(<c>wham/usage</c>) 조회 결과(#198). 로컬 로그로 폴백했을 때 그 이유를 화면에 알리는 데 쓴다.
    /// 다른 공급자 스냅샷에서는 기본값(<see cref="Models.CodexApiStatus.NotAttempted"/>) 그대로다.
    /// </summary>
    public CodexApiStatus CodexApiStatus { get; set; }
}

/// <summary>
/// Codex Direct API 조회 결과(#198). "못 읽었다" 를 한 덩어리로 묶으면 로그인 만료와 일시적 네트워크 실패를
/// 구분할 수 없어, 만료된 PC 에서 게이지가 옛 값·빈 값이 되고 모델별 추가 한도가 아무 설명 없이 사라진다.
/// </summary>
public enum CodexApiStatus
{
    /// <summary>조회하지 않았다(Direct API 비활성).</summary>
    NotAttempted,
    /// <summary>공식 값을 읽었다.</summary>
    Succeeded,
    /// <summary>로그인 파일이 없거나 access_token 이 없다 — 로그아웃·API 키 모드 등.</summary>
    NoCredentials,
    /// <summary>서버가 토큰을 거부했다(만료·폐기) — Codex 를 실행해 갱신하거나 다시 로그인해야 한다.</summary>
    AuthFailed,
    /// <summary>그 밖의 HTTP 오류·네트워크 실패·응답 해석 실패 — 일시적일 수 있다.</summary>
    Unavailable,
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

/// <summary>
/// 콘솔 <c>go/status</c> 한 번의 관측. 할당량 창 말고도 같은 응답에 든 요금제·구독 상태와,
/// 관리자일 때 따로 읽는 추가 사용·크레딧을 함께 싣는다(값을 보태려고 <c>with</c> 로 복사하므로 record).
/// </summary>
public sealed record OpenCodeWebUsage
{
    public DateTimeOffset? ObservedAtUtc { get; init; }
    public OpenCodeQuotaWindow Rolling { get; init; } = new();
    public OpenCodeQuotaWindow Weekly { get; init; } = new();
    public OpenCodeQuotaWindow Monthly { get; init; } = new();

    /// <summary>요금제 — 콘솔 응답의 <c>product</c>("go"·"go-plus") 그대로. 모르면 null.</summary>
    public string? Product { get; init; }

    /// <summary>
    /// 추가 사용 — 한도를 넘기면 작업공간 크레딧으로 이어 쓰는가. 콘솔이 "Extra usage" 를 켜짐으로
    /// 보여주는 조건과 같다: 설정이 켜져 있고, 관리자라면 쓸 크레딧이 남아 있을 때.
    /// </summary>
    public bool ExtraUsageEnabled { get; init; }

    /// <summary>작업공간의 사용 가능 크레딧(USD). 콘솔이 관리자에게만 보여주는 값이라 그 밖엔 null.</summary>
    public decimal? CreditsUsd { get; init; }

    /// <summary>구독 상태 — 정상 갱신이면 <see cref="OpenCodeGoSubscriptionStatus.Renews"/>.</summary>
    public OpenCodeGoSubscriptionStatus Subscription { get; init; }

    /// <summary>현재 구독 기간 끝(<c>access.endsAt</c>). 해지·Go 전환 예정 문구에 쓴다.</summary>
    public DateTimeOffset? PeriodEndsAt { get; init; }
}

/// <summary>
/// 콘솔 Go 카드의 상태 표시와 같은 구분. 앞의 다섯은 할당량을 쓸 수 있는 상태(<c>access</c> 있음),
/// 뒤의 넷은 구독은 있지만 지금은 못 쓰는 상태(<c>access: null</c>) 다.
/// </summary>
public enum OpenCodeGoSubscriptionStatus
{
    /// <summary>정상 — 기간 끝에 갱신된다.</summary>
    Renews,
    /// <summary>갱신 결제가 실패해 재시도를 기다린다(<c>renewalRetryAt</c>).</summary>
    PaymentFailed,
    /// <summary>다음 갱신에 결제 수단 조치가 필요하다(<c>renewalAuthorizationRequired</c>).</summary>
    ActionNeeded,
    /// <summary>해지 예약 — 기간 끝에 끝난다(<c>cancelAtPeriodEnd</c>).</summary>
    Ends,
    /// <summary>Go Plus 에서 기간 끝에 Go 로 내려간다.</summary>
    SwitchesToGo,
    /// <summary>은행의 갱신 결제 승인을 기다린다(<c>renewalPaymentAttemptID</c>).</summary>
    ApprovalNeeded,
    /// <summary>갱신 결제가 처리 중이다(<c>renewalPending</c>).</summary>
    RenewalProcessing,
    /// <summary>결제 승인이 필요해 구독이 만료됐다 — 다시 활성화해야 한다.</summary>
    ReauthorizationNeeded,
    /// <summary>갱신 결제가 성공할 때까지 멈췄다.</summary>
    Paused,
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

/// <param name="Inactive">
/// <see cref="OpenCodeWebSessionState.NoQuota"/> 일 때, 구독은 있지만 지금 못 쓰는 이유(일시정지 등).
/// 구독 자체가 없으면 null.
/// </param>
public readonly record struct OpenCodeWebReadResult(
    OpenCodeWebUsage? Usage, OpenCodeWebSessionState State, OpenCodeGoSubscriptionStatus? Inactive = null)
{
    public static OpenCodeWebReadResult NotAttempted => new(null, OpenCodeWebSessionState.Unknown);
    public static OpenCodeWebReadResult SignedOut => new(null, OpenCodeWebSessionState.SignedOut);
    public static OpenCodeWebReadResult Unavailable => new(null, OpenCodeWebSessionState.Unavailable);
    public static OpenCodeWebReadResult NoQuota => new(null, OpenCodeWebSessionState.NoQuota);
    public static OpenCodeWebReadResult NoQuotaBecause(OpenCodeGoSubscriptionStatus? inactive) =>
        new(null, OpenCodeWebSessionState.NoQuota, inactive);
    public static OpenCodeWebReadResult Success(OpenCodeWebUsage usage) =>
        new(usage, OpenCodeWebSessionState.Authenticated);
}

public class OpenCodePeriodUsage
{
    public long Tokens { get; set; }
    public int Requests { get; set; }
    public decimal CostUsd { get; set; }
}
