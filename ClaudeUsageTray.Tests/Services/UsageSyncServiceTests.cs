using System.IO;
using System.Linq;
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
    public void OpenCodeOfficialQuota_IsAvailableToAnotherDeviceWithoutCredentials()
    {
        var now = new DateTimeOffset(2026, 8, 10, 4, 0, 0, TimeSpan.Zero);
        var desktop = CreateService("desktop", now);
        var laptop = CreateService("laptop", now.AddMinutes(1));
        var syncRoot = Path.Combine(_tempRoot, "sync");

        desktop.WriteSnapshot(syncRoot, desktop.CreateSnapshot(
            UsageProviderKind.OpenCode,
            null,
            new UsageSyncQuotaSnapshot
            {
                HasData = true,
                OpenCode = new UsageSyncOpenCodeQuota
                {
                    Rolling = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.12, ResetAt = now.AddHours(3) },
                    Weekly = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.34, ResetAt = now.AddDays(5) },
                    Monthly = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.56, ResetAt = now.AddDays(21) },
                },
            },
            new UsageSyncLocalTotals { InputTokens = 100 }));

        var read = laptop.ReadSnapshots(syncRoot, UsageProviderKind.OpenCode, null, new DateOnly(2026, 8, 10));
        var newest = laptop.SelectNewestQuotaSnapshot(read.Snapshots, TimeSpan.FromMinutes(5));

        Assert.NotNull(newest?.Quota?.OpenCode);
        Assert.Equal(0.12, newest!.Quota!.OpenCode!.Rolling.UsagePercent, 6);
        Assert.Equal(now.AddDays(5), newest.Quota.OpenCode.Weekly.ResetAt);
        Assert.Equal(0.56, newest.Quota.OpenCode.Monthly.UsagePercent, 6);
        var json = ReadAllTextShared(Directory.EnumerateFiles(syncRoot, "*.json", SearchOption.AllDirectories).Single());
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("workspace", json, StringComparison.OrdinalIgnoreCase);
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

    // DeviceCount 는 "기기 수" 가 아니라 "사용량이 있는 기기 수" 다.
    // 이 PC 에서 OpenCode 를 아예 안 쓰면 이 PC 스냅샷은 집계에서 빠져 DeviceCount 가 1 이 되고,
    // 그래도 다른 PC 의 합계는 그대로 살아 있어야 한다. (MainViewModel.HasMergedDeviceTotals 가 이 값을 본다)
    [Fact]
    public void MergeLocalTotals_CountsOnlyDevicesWithUsage()
    {
        var now = new DateTimeOffset(2026, 8, 10, 6, 0, 0, TimeSpan.Zero);
        var desktop = CreateService("desktop", now);
        var idleLaptop = CreateService("idle-laptop", now.AddMinutes(1));
        var syncRoot = Path.Combine(_tempRoot, "sync");

        desktop.WriteSnapshot(syncRoot, desktop.CreateSnapshot(
            UsageProviderKind.OpenCode,
            null,
            null,
            new UsageSyncLocalTotals { InputTokens = 248_796, OutputTokens = 42_958, RequestCount = 179 }));
        idleLaptop.WriteSnapshot(syncRoot, idleLaptop.CreateSnapshot(
            UsageProviderKind.OpenCode,
            null,
            null,
            new UsageSyncLocalTotals(),
            "no_usage"));

        var read = idleLaptop.ReadSnapshots(syncRoot, UsageProviderKind.OpenCode, null, new DateOnly(2026, 8, 10));
        var merged = idleLaptop.MergeLocalTotals(read.Snapshots, TimeSpan.FromHours(24));

        Assert.Equal(2, read.Snapshots.Count);
        Assert.Equal(1, merged.DeviceCount);
        Assert.True(merged.HasData);
        Assert.Equal(248_796, merged.InputTokens);
        Assert.Equal(42_958, merged.OutputTokens);
        Assert.Equal(179, merged.RequestCount);
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

    // 시간선 마커 위치는 (리셋 시각 - 창 길이) 로 역산하므로, 창 길이가 함께 건너오지 않으면
    // 받는 PC 는 5시간으로 가정할 수밖에 없고 주간 창 계정에서 마커가 통째로 어긋난다.
    [Fact]
    public void QuotaSnapshot_CarriesWindowLengths_AcrossDevices()
    {
        var now = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var desktop = CreateService("desktop", now);
        var syncRoot = Path.Combine(_tempRoot, "sync");

        desktop.WriteSnapshot(syncRoot, desktop.CreateSnapshot(
            UsageProviderKind.Codex,
            null,
            new UsageSyncQuotaSnapshot
            {
                HasData = true,
                ShortUsagePercent = 0.61,
                ShortResetAt = now.AddHours(3),
                ShortWindowMinutes = 10080,
                LongUsagePercent = 0.2,
                LongResetAt = now.AddDays(2),
                LongWindowMinutes = 43200,
                HasLongWindow = true,
                PlanType = "Plus",
            },
            new UsageSyncLocalTotals()));

        var laptop = CreateService("laptop", now.AddMinutes(1));
        var read = laptop.ReadSnapshots(syncRoot, UsageProviderKind.Codex, null, new DateOnly(2026, 7, 9));
        var newest = laptop.SelectNewestQuotaSnapshot(read.Snapshots, TimeSpan.FromMinutes(5));

        Assert.NotNull(newest);
        Assert.Equal(10080, newest!.Quota!.ShortWindowMinutes);
        Assert.Equal(43200, newest.Quota.LongWindowMinutes);
        Assert.True(newest.Quota.HasLongWindow);
        Assert.Equal("Plus", newest.Quota.PlanType);
        // 받은 창 길이로 역산해야 마커가 제자리에 선다. 주간 창인데 5시간으로 가정하면
        // 같은 리셋 시각이 98% 경과가 아니라 40% 경과로 계산돼 마커가 막대 한가운데로 밀린다.
        var window = UsageCalculator.WindowSpan(newest.Quota.ShortWindowMinutes, TimeSpan.FromHours(5));
        var correct = UsageCalculator.TimeProgress(newest.Quota.ShortResetAt, window, now);
        var assumedFiveHour = UsageCalculator.TimeProgress(newest.Quota.ShortResetAt, TimeSpan.FromHours(5), now);

        Assert.NotNull(correct);
        Assert.Equal(1.0 - 180.0 / 10080.0, correct!.Value, 6);
        Assert.Equal(0.4, assumedFiveHour!.Value, 6);
    }

    // Antigravity 는 토큰 합계가 없고 모델별 잔여 할당량만 있다 — 그 목록이 그대로 건너와야
    // 로그인하지 않은 PC 에서도 같은 패널을 그릴 수 있다.
    [Fact]
    public void QuotaSnapshot_CarriesAntigravityModelRows()
    {
        var now = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var desktop = CreateService("desktop", now);
        var syncRoot = Path.Combine(_tempRoot, "sync");

        desktop.WriteSnapshot(syncRoot, desktop.CreateSnapshot(
            UsageProviderKind.Antigravity,
            "user@example.com",
            new UsageSyncQuotaSnapshot
            {
                HasData = true,
                TierName = "Gemini Code Assist",
                Models =
                [
                    new UsageSyncModelQuota { ModelId = "gemini-3-pro", RemainingFraction = 0.25, ResetAt = now.AddHours(4) },
                    new UsageSyncModelQuota { ModelId = "claude-sonnet-4-5", RemainingFraction = 0.8, ResetAt = now.AddHours(4) },
                ],
            },
            new UsageSyncLocalTotals()));

        var laptop = CreateService("laptop", now.AddMinutes(1));
        var read = laptop.ReadSnapshots(syncRoot, UsageProviderKind.Antigravity, "user@example.com", new DateOnly(2026, 7, 9));
        var newest = laptop.SelectNewestQuotaSnapshot(read.Snapshots, TimeSpan.FromMinutes(5));

        Assert.NotNull(newest);
        Assert.Equal("Gemini Code Assist", newest!.Quota!.TierName);
        Assert.Equal(2, newest.Quota.Models.Length);
        Assert.Equal("gemini-3-pro", newest.Quota.Models[0].ModelId);
        Assert.Equal(0.25, newest.Quota.Models[0].RemainingFraction, 6);

        // 계정 키를 넘기더라도 원문은 파일에 남지 않는다(경로·본문 모두 해시).
        var written = Directory.EnumerateFiles(syncRoot, "*.json", SearchOption.AllDirectories).Single();
        Assert.DoesNotContain("user@example.com", ReadAllTextShared(written), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("user@example.com", written, StringComparison.OrdinalIgnoreCase);
    }

    // 스키마 버전은 정확히 일치할 때만 읽히므로, 필드 추가만으로는 올리지 않는다.
    // 올리면 롤아웃 중 구/신 버전 PC 가 서로의 스냅샷을 통째로 무시해 동기화가 끊긴다.
    [Fact]
    public void ReadSnapshots_AcceptsFilesWrittenWithoutTheNewOptionalQuotaFields()
    {
        var now = new DateTimeOffset(2026, 7, 9, 9, 0, 0, TimeSpan.Zero);
        var reader = CreateService("reader", now);
        var syncRoot = Path.Combine(_tempRoot, "sync");
        var accountHash = UsageSyncService.BuildAccountHash(UsageProviderKind.Codex, null);
        var directory = Path.Combine(syncRoot, accountHash, UsageProviderKind.Codex, "2026-07-09");
        Directory.CreateDirectory(directory);

        // 구버전(v1.37.x)이 쓴 모양 — windowMinutes / models 필드가 아예 없다.
        WriteAllTextShared(Path.Combine(directory, "olddevice.json"),
            """
            {"schemaVersion":1,"accountHash":"REPLACE","provider":"codex","deviceId":"olddevice","deviceName":"old",
             "localDate":"2026-07-09","observedAtUtc":"2026-07-09T08:59:00Z",
             "quota":{"hasData":true,"observedAtUtc":"2026-07-09T08:59:00Z","shortUsagePercent":0.4,
                      "shortResetAt":"2026-07-09T12:00:00Z"},
             "localTotals":{"inputTokens":5}}
            """.Replace("REPLACE", accountHash));

        var read = reader.ReadSnapshots(syncRoot, UsageProviderKind.Codex, null, new DateOnly(2026, 7, 9));
        var newest = reader.SelectNewestQuotaSnapshot(read.Snapshots, TimeSpan.FromMinutes(5));

        Assert.Empty(read.Diagnostics);
        Assert.NotNull(newest);
        Assert.Equal(0.4, newest!.Quota!.ShortUsagePercent, 6);
        Assert.Null(newest.Quota.ShortWindowMinutes);
        Assert.Empty(newest.Quota.Models);
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
