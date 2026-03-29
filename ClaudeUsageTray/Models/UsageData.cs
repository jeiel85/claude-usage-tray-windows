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
    public bool HasRateLimitHit { get; set; }
    public string? RateLimitResetTime { get; set; }

    // 시간대별(0~23시) 전체 토큰 집계
    public long[] HourlyTokens { get; } = new long[24];

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;
    public long GrandTotal => TotalInputTokens + TotalOutputTokens + TotalCacheReadTokens + TotalCacheWriteTokens;
}
