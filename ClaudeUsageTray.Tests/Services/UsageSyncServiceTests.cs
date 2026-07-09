using System.IO;
using System.Text;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public sealed class UsageSyncServiceTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"usage-sync-tests-{Guid.NewGuid():N}");

    [Fact]
    public void CreateAndWriteSnapshot_DoesNotPersistRawAccountKeyOrCredentialFields()
    {
        var service = CreateService("desktop");
        var syncRoot = Path.Combine(_tempRoot, "sync");
        var snapshot = service.CreateSnapshot(
            UsageProviderKind.Claude,
            "org-plain-id",
            new UsageSyncQuotaSnapshot { HasData = true, ShortUsagePercent = 0.42 },
            new UsageSyncLocalTotals { InputTokens = 100, OutputTokens = 50 });

        var path = service.WriteSnapshot(syncRoot, snapshot);
        var json = ReadAllTextShared(path);

        Assert.DoesNotContain("org-plain-id", json, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawLog", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(UsageSyncService.BuildAccountHash(UsageProviderKind.Claude, "org-plain-id"), snapshot.AccountHash);
    }

    [Fact]
    public void ReadSnapshots_IgnoresCorruptAndWrongContractFiles()
    {
        var desktop = CreateService("desktop");
        var laptop = CreateService("laptop");
        var syncRoot = Path.Combine(_tempRoot, "sync");

        var desktopSnapshot = desktop.CreateSnapshot(
            UsageProviderKind.Codex,
            null,
            null,
            new UsageSyncLocalTotals { InputTokens = 10, OutputTokens = 20 });
        var laptopSnapshot = laptop.CreateSnapshot(
            UsageProviderKind.Codex,
            null,
            null,
            new UsageSyncLocalTotals { InputTokens = 30, OutputTokens = 40 });

        var firstPath = desktop.WriteSnapshot(syncRoot, desktopSnapshot);
        laptop.WriteSnapshot(syncRoot, laptopSnapshot);
        WriteAllTextShared(Path.Combine(Path.GetDirectoryName(firstPath)!, "corrupt.json"), "{not-json");
        WriteAllTextShared(
            Path.Combine(Path.GetDirectoryName(firstPath)!, "wrong-provider.json"),
            """
            {"schemaVersion":1,"accountHash":"x","provider":"claude","deviceId":"bad","localDate":"2026-07-09","observedAtUtc":"2026-07-09T01:00:00Z","localTotals":{}}
            """);

        var result = desktop.ReadSnapshots(syncRoot, UsageProviderKind.Codex, null, new DateOnly(2026, 7, 9));

        Assert.Equal(2, result.Snapshots.Count);
        Assert.Equal(2, result.Diagnostics.Count);
    }

    [Fact]
    public void MergeLocalTotals_SumsFreshLatestDeviceSnapshotsOnly()
    {
        var now = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var desktop = CreateService("desktop", now);
        var laptop = CreateService("laptop", now.AddMinutes(-1));
        var stale = CreateService("old-laptop", now.AddHours(-25));
        var syncRoot = Path.Combine(_tempRoot, "sync");

        desktop.WriteSnapshot(syncRoot, desktop.CreateSnapshot(
            UsageProviderKind.GeminiCli,
            null,
            new UsageSyncQuotaSnapshot { HasData = true, ShortUsagePercent = 0.2 },
            new UsageSyncLocalTotals { InputTokens = 100, OutputTokens = 200, RequestCount = 2, HourlyTokens = Hourly(3, 300) }));
        laptop.WriteSnapshot(syncRoot, laptop.CreateSnapshot(
            UsageProviderKind.GeminiCli,
            null,
            new UsageSyncQuotaSnapshot { HasData = true, ShortUsagePercent = 0.7 },
            new UsageSyncLocalTotals { InputTokens = 10, OutputTokens = 20, RequestCount = 1, HourlyTokens = Hourly(3, 30) }));
        stale.WriteSnapshot(syncRoot, stale.CreateSnapshot(
            UsageProviderKind.GeminiCli,
            null,
            new UsageSyncQuotaSnapshot { HasData = true, ShortUsagePercent = 0.9 },
            new UsageSyncLocalTotals { InputTokens = 999, OutputTokens = 999, RequestCount = 9 }));

        var reader = CreateService("reader", now);
        var result = reader.ReadSnapshots(syncRoot, UsageProviderKind.GeminiCli, null, new DateOnly(2026, 7, 9));
        var merged = reader.MergeLocalTotals(result.Snapshots, TimeSpan.FromHours(24));
        var newestQuota = reader.SelectNewestQuotaSnapshot(result.Snapshots, TimeSpan.FromHours(24));

        Assert.Equal(2, merged.DeviceCount);
        Assert.Equal(110, merged.InputTokens);
        Assert.Equal(220, merged.OutputTokens);
        Assert.Equal(3, merged.RequestCount);
        Assert.Equal(330, merged.HourlyTokens[3]);
        Assert.NotNull(newestQuota);
        Assert.Equal("desktop", newestQuota!.DeviceName);
    }

    [Fact]
    public void WriteSnapshot_PreservesPreviousSuccessfulQuotaWhenCurrentWriteHasOnlyLocalTotals()
    {
        var now = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var service = new UsageSyncService(Path.Combine(_tempRoot, "appdata", "desktop"), () => now, "desktop");
        var syncRoot = Path.Combine(_tempRoot, "sync");

        service.WriteSnapshot(syncRoot, service.CreateSnapshot(
            UsageProviderKind.Claude,
            "org",
            new UsageSyncQuotaSnapshot { HasData = true, ShortUsagePercent = 0.4 },
            new UsageSyncLocalTotals { InputTokens = 10 }));

        now = now.AddMinutes(1);
        service.WriteSnapshot(syncRoot, service.CreateSnapshot(
            UsageProviderKind.Claude,
            "org",
            null,
            new UsageSyncLocalTotals { InputTokens = 20 }));

        var result = service.ReadSnapshots(syncRoot, UsageProviderKind.Claude, "org", new DateOnly(2026, 7, 9));
        var newestQuota = service.SelectNewestQuotaSnapshot(result.Snapshots, TimeSpan.FromMinutes(5));
        var merged = service.MergeLocalTotals(result.Snapshots, TimeSpan.FromHours(24));

        Assert.NotNull(newestQuota);
        Assert.Equal(0.4, newestQuota!.Quota!.ShortUsagePercent);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero), newestQuota.Quota.ObservedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 7, 9, 9, 1, 0, TimeSpan.Zero), newestQuota.ObservedAtUtc);
        Assert.Equal(20, merged.InputTokens);
    }

    [Fact]
    public void NotificationSettings_DefaultsKeepUsageSyncDisabled()
    {
        var settings = new NotificationSettings();

        Assert.False(settings.UsageSyncEnabled);
        Assert.Equal("", settings.UsageSyncFolderPath);
        Assert.Equal(UsageSyncService.DefaultApiSnapshotTtlMinutes, settings.UsageSyncApiSnapshotTtlMinutes);
        Assert.Equal(UsageSyncService.DefaultLocalSnapshotTtlHours, settings.UsageSyncLocalSnapshotTtlHours);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private UsageSyncService CreateService(string deviceName) =>
        CreateService(deviceName, new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero));

    private UsageSyncService CreateService(string deviceName, DateTimeOffset now) =>
        new(Path.Combine(_tempRoot, "appdata", deviceName), () => now, deviceName);

    private static long[] Hourly(int hour, long value)
    {
        var hourly = new long[24];
        hourly[hour] = value;
        return hourly;
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void WriteAllTextShared(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }
}
