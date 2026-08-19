using System.Text.Json.Serialization;

namespace ClaudeUsageTray.Models;

public class UsageResponse
{
    [JsonPropertyName("five_hour")]
    public UsageWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")]
    public UsageWindow? SevenDay { get; set; }

    [JsonPropertyName("seven_day_opus")]
    public UsageWindow? SevenDayOpus { get; set; }

    [JsonPropertyName("seven_day_sonnet")]
    public UsageWindow? SevenDaySonnet { get; set; }

    [JsonPropertyName("extra_usage")]
    public ExtraUsage? ExtraUsage { get; set; }
}

public class UsageWindow
{
    [JsonPropertyName("utilization")]
    public double Utilization { get; set; }

    [JsonPropertyName("resets_at")]
    public string? ResetsAt { get; set; }

    public double UsagePercent => Math.Min(1.0, Utilization / 100.0);

    public DateTimeOffset? ResetsAtParsed => ResetsAt != null
        ? DateTimeOffset.TryParse(ResetsAt, out var dt) ? dt : null
        : null;
}

public class ExtraUsage
{
    [JsonPropertyName("is_enabled")]
    public bool IsEnabled { get; set; }

    [JsonPropertyName("utilization")]
    public double? Utilization { get; set; }

    [JsonPropertyName("used_credits")]
    public double? UsedCredits { get; set; }

    [JsonPropertyName("monthly_limit")]
    public double? MonthlyLimit { get; set; }
}

public class SessionStats
{
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheWriteTokens { get; set; }
    public int SessionCount { get; set; }
    public DateTime LastActivity { get; set; }

    /// <summary>
    /// 오늘 기록이 있는 세션들의 상세. SessionCount 와 같은 것을 세지만, 목록으로 보여주려면
    /// 세션마다 어느 프로젝트·브랜치였는지가 필요하다. (이 PC 의 트랜스크립트 기준)
    /// </summary>
    public List<SessionInfo> Sessions { get; } = [];
    public bool HasRateLimitHit { get; set; }
    public string? RateLimitResetTime { get; set; }

    // 시간대별(0~23시) 전체 토큰 집계
    public long[] HourlyTokens { get; } = new long[24];

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;
    public long GrandTotal => TotalInputTokens + TotalOutputTokens + TotalCacheReadTokens + TotalCacheWriteTokens;
}

/// <summary>
/// 오늘 활동이 있었던 세션 하나. 트랜스크립트 파일(*.jsonl) 1개 = 세션 1개.
/// </summary>
public class SessionInfo
{
    /// <summary>세션 UUID. 트랜스크립트에 없으면 파일명으로 채운다.</summary>
    public string SessionId { get; set; } = "";

    /// <summary>세션이 실행된 작업 디렉터리(cwd). 목록의 이름·툴팁이 여기서 나온다.</summary>
    public string ProjectPath { get; set; } = "";

    /// <summary>마지막으로 기록된 git 브랜치. 워크트리 구분에 쓴다.</summary>
    public string GitBranch { get; set; } = "";

    /// <summary>첫 사용자 프롬프트 한 줄 요약 — 세션을 알아보게 하는 제목.</summary>
    public string Title { get; set; } = "";

    /// <summary>오늘 마지막 활동 시각(UTC).</summary>
    public DateTime LastActivityUtc { get; set; }

    /// <summary>오늘 이 세션이 쓴 토큰 합계(입력+출력+캐시).</summary>
    public long TotalTokens { get; set; }
}
