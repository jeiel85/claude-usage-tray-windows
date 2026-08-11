using System.Text.Json.Serialization;

namespace ClaudeUsageTray.Models;

public static class UsageSyncSchema
{
    /// <summary>
    /// 스냅샷 계약 버전. <c>IsValidSnapshot</c> 이 정확히 일치하는 버전만 받아들이므로,
    /// 이 값을 올리면 구버전과 신버전 PC 가 서로의 스냅샷을 통째로 무시한다(롤아웃 중 동기화 단절).
    /// 따라서 필드 <b>추가</b>만으로 끝나는 변경에는 올리지 않는다 — System.Text.Json 은 모르는 필드를
    /// 무시하고 없는 필드는 기본값으로 두므로 양방향 호환된다. 기존 필드의 의미가 바뀔 때만 올린다.
    /// </summary>
    public const int CurrentVersion = 1;
}

public sealed class UsageSyncSnapshot
{
    public int SchemaVersion { get; set; } = UsageSyncSchema.CurrentVersion;
    public string AccountHash { get; set; } = "";
    public string Provider { get; set; } = "";
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string LocalDate { get; set; } = "";
    public DateTimeOffset ObservedAtUtc { get; set; }
    public UsageSyncQuotaSnapshot? Quota { get; set; }
    public UsageSyncLocalTotals LocalTotals { get; set; } = new();
    public string ErrorKind { get; set; } = "";
    public string Source { get; set; } = "local";
}

/// <summary>
/// 계정 단위 할당량 스냅샷 — 같은 계정이면 어느 PC 에서 봐도 같은 값이라 기기 간 공유가 성립한다.
/// (기기별로 계산되는 값 — 예: 로컬 토큰 합계 대비 목표치 — 은 여기 담지 않는다. <see cref="UsageSyncLocalTotals"/> 참고)
/// </summary>
public sealed class UsageSyncQuotaSnapshot
{
    public bool HasData { get; set; }
    public DateTimeOffset? ObservedAtUtc { get; set; }
    public double ShortUsagePercent { get; set; }
    public DateTimeOffset? ShortResetAt { get; set; }
    public bool IsShortResetEstimated { get; set; }
    public double LongUsagePercent { get; set; }
    public DateTimeOffset? LongResetAt { get; set; }
    public double? ExtraUsagePercent { get; set; }
    public bool ExtraUsageEnabled { get; set; }
    public bool ExtraHasLimit { get; set; }
    public string ExtraCreditsLabel { get; set; } = "";
    public string PlanType { get; set; } = "";

    // 창 길이 — 시간선 마커 위치는 (리셋 시각 - 창 길이) 로 역산하므로 받는 쪽에서도 반드시 필요하다.
    // Codex 는 계정에 따라 5시간이 아니라 주간 창을 쓰기 때문에 하드코딩할 수 없다.
    public int? ShortWindowMinutes { get; set; }
    public int? LongWindowMinutes { get; set; }
    public bool HasLongWindow { get; set; }

    // Antigravity 전용 — 모델별 잔여 할당량 목록. 다른 provider 에서는 비어 있다.
    public string TierName { get; set; } = "";
    public string PaidTierName { get; set; } = "";
    public UsageSyncModelQuota[] Models { get; set; } = [];

    // OpenCode Go 전용 — 공식 웹 콘솔이 제공한 계정 단위 할당량만 담는다.
    // 인증 쿠키·워크스페이스 URL 같은 로그인 정보는 동기화하지 않는다.
    public UsageSyncOpenCodeQuota? OpenCode { get; set; }
}

/// <summary>Antigravity 의 모델 한 개짜리 할당량 행(계정 단위).</summary>
public sealed class UsageSyncModelQuota
{
    public string ModelId { get; set; } = "";
    public double RemainingFraction { get; set; }
    public DateTimeOffset? ResetAt { get; set; }

    /// <summary>서버가 내려준 표시 이름. 이 필드가 없던 버전이 올린 스냅샷에서는 비어 있다.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>창 종류("weekly", "5h"). 받는 쪽에서 이름을 자기 언어로 부르는 데 쓴다.</summary>
    public string Window { get; set; } = "";
}

public sealed class UsageSyncOpenCodeQuota
{
    public UsageSyncOpenCodeQuotaWindow Rolling { get; set; } = new();
    public UsageSyncOpenCodeQuotaWindow Weekly { get; set; } = new();
    public UsageSyncOpenCodeQuotaWindow Monthly { get; set; } = new();
}

public sealed class UsageSyncOpenCodeQuotaWindow
{
    public double UsagePercent { get; set; }
    public DateTimeOffset ResetAt { get; set; }
}

public sealed class UsageSyncLocalTotals
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public int SessionCount { get; set; }
    public int RequestCount { get; set; }
    public long[] HourlyTokens { get; set; } = new long[24];

    [JsonIgnore]
    public bool HasData =>
        InputTokens != 0 ||
        OutputTokens != 0 ||
        CacheReadTokens != 0 ||
        CacheWriteTokens != 0 ||
        SessionCount != 0 ||
        RequestCount != 0 ||
        HourlyTokens.Any(static value => value != 0);
}

public sealed record UsageSyncReadDiagnostic(string Path, string Reason);

public sealed class UsageSyncReadResult
{
    public IReadOnlyList<UsageSyncSnapshot> Snapshots { get; init; } = [];
    public IReadOnlyList<UsageSyncReadDiagnostic> Diagnostics { get; init; } = [];
}

public sealed class UsageSyncMergedLocalTotals
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public int SessionCount { get; set; }
    public int RequestCount { get; set; }
    public long[] HourlyTokens { get; set; } = new long[24];
    public int DeviceCount { get; set; }
    public DateTimeOffset? LatestObservedAtUtc { get; set; }

    public bool HasData => DeviceCount > 0;
}
