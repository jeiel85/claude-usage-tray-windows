using System.Text.Json.Serialization;

namespace ClaudeUsageTray.Models;

public class UsageResponse
{
    [JsonPropertyName("usage")]
    public List<UsageBucket> Usage { get; set; } = new();
}

public class UsageBucket
{
    [JsonPropertyName("bucket")]
    public string? Bucket { get; set; }

    [JsonPropertyName("remaining_credits")]
    public long? RemainingCredits { get; set; }

    [JsonPropertyName("used_credits")]
    public long? UsedCredits { get; set; }

    [JsonPropertyName("max_credits")]
    public long? MaxCredits { get; set; }

    [JsonPropertyName("reset_at")]
    public string? ResetAt { get; set; }

    [JsonPropertyName("model_usage")]
    public Dictionary<string, long>? ModelUsage { get; set; }

    // Computed
    public double UsagePercent
    {
        get
        {
            if (MaxCredits is null or 0) return 0;
            var used = UsedCredits ?? (MaxCredits - RemainingCredits) ?? 0;
            return Math.Min(1.0, (double)used / MaxCredits.Value);
        }
    }

    public long UsedAmount => UsedCredits ?? (MaxCredits.HasValue && RemainingCredits.HasValue
        ? MaxCredits.Value - RemainingCredits.Value : 0);

    public DateTimeOffset? ResetsAt => ResetAt != null
        ? DateTimeOffset.TryParse(ResetAt, out var dt) ? dt : null
        : null;
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

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;
    public long GrandTotal => TotalInputTokens + TotalOutputTokens + TotalCacheReadTokens + TotalCacheWriteTokens;
}
