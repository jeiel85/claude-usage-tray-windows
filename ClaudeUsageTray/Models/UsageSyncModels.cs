using System.Text.Json.Serialization;

namespace ClaudeUsageTray.Models;

public static class UsageSyncSchema
{
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
