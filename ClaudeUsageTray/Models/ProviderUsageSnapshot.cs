namespace ClaudeUsageTray.Models;

public class ProviderUsageSnapshot
{
    public bool HasData { get; set; }
    public bool IsLimited { get; set; }
    public string? ErrorMessage { get; set; }

    public double ShortUsagePercent { get; set; }
    public DateTimeOffset? ShortResetAt { get; set; }
    public bool IsShortResetEstimated { get; set; }

    public double LongUsagePercent { get; set; }
    public DateTimeOffset? LongResetAt { get; set; }

    public string? PlanType { get; set; }
    public string? DataSource { get; set; }

    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalCacheReadTokens { get; set; }
    public long TotalCacheWriteTokens { get; set; }

    public int SessionCount { get; set; }
    public int RequestCount { get; set; }

    public long[] HourlyTokens { get; set; } = new long[24];
}
