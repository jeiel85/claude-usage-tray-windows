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

public class OpenCodePeriodUsage
{
    public long Tokens { get; set; }
    public int Requests { get; set; }
    public decimal CostUsd { get; set; }
}
