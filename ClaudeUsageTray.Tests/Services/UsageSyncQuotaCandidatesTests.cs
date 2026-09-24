using System.IO;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 다른 PC 에서 동기화된 할당량을 로컬 조회 실패 시 최후 폴백으로 쓸지 판정하는
/// <see cref="UsageSyncService.IsQuotaWindowStillActive"/> 를 provider 별 스냅샷 모양마다 검증한다.
/// 사용량 % 는 창이 끝날 때까지 증가만 하므로, 창이 이미 리셋됐다면
/// 오래된 스냅샷을 보여주는 순간 실제보다 부풀려 보인다 — 이 경계를 놓치면 안 된다.
/// </summary>
public class UsageSyncQuotaWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 3, 0, 0, TimeSpan.Zero);

    private static UsageSyncSnapshot Snapshot(UsageSyncQuotaSnapshot? quota, string provider = UsageProviderKind.Claude) => new()
    {
        Provider = provider,
        DeviceName = "OTHER-PC",
        ObservedAtUtc = Now,
        Quota = quota,
    };

    [Fact]
    public void Null_snapshot_is_not_active()
    {
        Assert.False(UsageSyncService.IsQuotaWindowStillActive(null, Now));
    }

    [Fact]
    public void Snapshot_without_quota_data_is_not_active()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot { HasData = false });
        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Quota_with_future_reset_is_still_active()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.21,
            ShortResetAt = Now.AddHours(2),
        });

        Assert.True(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Quota_whose_reset_already_passed_is_rejected()
    {
        // 노트북이 87분 전에 관측한 값이라도, 5시간 창이 그 사이 이미 리셋됐다면
        // 그 %는 실제로는 지나간 값이라 최후 폴백으로도 쓰면 안 된다.
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.87,
            ShortResetAt = Now.AddMinutes(-5),
        });

        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Quota_whose_long_window_reset_already_passed_is_rejected()
    {
        // 5시간 창은 아직 안 끝났어도, 7일 창이 그 사이 리셋됐다면 LongUsagePercent 는
        // 실제로는 0%대로 돌아갔을 값이라 최후 폴백으로 쓰면 안 된다.
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.3,
            ShortResetAt = Now.AddHours(2),
            LongUsagePercent = 0.95,
            LongResetAt = Now.AddMinutes(-1),
        });

        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Quota_with_unknown_reset_time_is_allowed()
    {
        // 리셋 시각을 모르는 구버전 스냅샷은 판단할 근거가 없으니 있는 그대로 허용한다.
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.5,
            ShortResetAt = null,
        });

        Assert.True(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    // Codex 주간 전용 플랜은 ShortResetAt 없이 LongResetAt 만 채운다(실측: shortResetAt null,
    // longWindowMinutes 10080). ShortResetAt 이 없다고 무조건 허용하면 안 되고, 있는 필드만 검사한다.
    [Fact]
    public void Codex_weekly_only_quota_with_future_long_reset_is_active()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortResetAt = null,
            LongUsagePercent = 0.28,
            LongResetAt = Now.AddDays(4),
        }, UsageProviderKind.Codex);

        Assert.True(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Codex_weekly_only_quota_whose_long_reset_already_passed_is_rejected()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortResetAt = null,
            LongUsagePercent = 0.99,
            LongResetAt = Now.AddMinutes(-1),
        }, UsageProviderKind.Codex);

        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    // Antigravity 는 Short/LongResetAt 없이 모델별 리셋만 담는다. 예전 판정은 이 모양을
    // "리셋을 모른다" 로 보고 무조건 통과시켰다 — 모델이 전부 리셋을 지났으면 거부해야 한다.
    [Fact]
    public void Antigravity_quota_with_a_model_still_in_window_is_active()
    {
        var snapshot = Snapshot(AntigravityQuota(Now.AddMinutes(-30), Now.AddHours(3)), UsageProviderKind.Antigravity);

        Assert.True(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Antigravity_quota_whose_models_all_reset_is_rejected()
    {
        var snapshot = Snapshot(AntigravityQuota(Now.AddMinutes(-30), Now.AddMinutes(-1)), UsageProviderKind.Antigravity);

        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Fact]
    public void Antigravity_quota_whose_models_have_no_reset_is_rejected()
    {
        // 화면(AntigravityViewModel.ApplyQuota)이 리셋을 모르는 모델을 건너뛰므로 보여줄 것이 없다.
        var snapshot = Snapshot(AntigravityQuota((DateTimeOffset?)null),UsageProviderKind.Antigravity);

        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    // OpenCode 는 롤링·주간·월간 세 창이 독립적으로 리셋된다 — 하나라도 지났으면 거부한다.
    [Fact]
    public void OpenCode_quota_with_all_windows_open_is_active()
    {
        var snapshot = Snapshot(OpenCodeQuota(Now.AddHours(2), Now.AddDays(3), Now.AddDays(20)), UsageProviderKind.OpenCode);

        Assert.True(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OpenCode_quota_with_any_window_reset_is_rejected(int resetWindow)
    {
        var resets = new[] { Now.AddHours(2), Now.AddDays(3), Now.AddDays(20) };
        resets[resetWindow] = Now.AddMinutes(-1);
        var snapshot = Snapshot(OpenCodeQuota(resets[0], resets[1], resets[2]), UsageProviderKind.OpenCode);

        Assert.False(UsageSyncService.IsQuotaWindowStillActive(snapshot, Now));
    }

    internal static UsageSyncQuotaSnapshot AntigravityQuota(params DateTimeOffset?[] modelResets) => new()
    {
        HasData = true,
        TierName = "Pro",
        Models = modelResets
            .Select((resetAt, i) => new UsageSyncModelQuota
            {
                ModelId = $"model-{i}",
                RemainingFraction = 0.5,
                ResetAt = resetAt,
                Window = "5h",
            })
            .ToArray(),
    };

    internal static UsageSyncQuotaSnapshot OpenCodeQuota(DateTimeOffset rolling, DateTimeOffset weekly, DateTimeOffset monthly) => new()
    {
        HasData = true,
        OpenCode = new UsageSyncOpenCodeQuota
        {
            Rolling = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.1, ResetAt = rolling },
            Weekly = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.2, ResetAt = weekly },
            Monthly = new UsageSyncOpenCodeQuotaWindow { UsagePercent = 0.3, ResetAt = monthly },
        },
    };
}

/// <summary>
/// "신선한 값 우선, 없으면 24시간 이내 관측 중 창이 유효한 것" 선택 규칙
/// (<see cref="UsageSyncService.SelectQuotaCandidates"/>) — 모든 provider 가 공유한다.
/// 최후 폴백이 신선한 값을 밀어내지 않는지, 신선한 값이 없을 때만 개입하는지, 자정 경계에서
/// 어제 폴더의 유효한 관측을 놓치지 않는지 실제 공유 폴더 쓰기/읽기로 검증한다.
/// </summary>
public sealed class UsageSyncQuotaCandidatesTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"usage-sync-candidates-{Guid.NewGuid():N}");
    private string SyncRoot => Path.Combine(_tempRoot, "sync");

    // 스냅샷의 날짜 폴더는 기기의 현지 날짜로 정해지므로, 시각도 현지 기준으로 만든다.
    private static DateTimeOffset Local(int day, int hour, int minute) =>
        new(new DateTime(2026, 9, day, hour, minute, 0, DateTimeKind.Local));

    [Fact]
    public void Fresh_value_wins_even_when_an_older_observation_exists()
    {
        var now = Local(24, 12, 0);
        WriteCodex("laptop", now.AddHours(-3), 0.9, resetAt: now.AddHours(1));
        WriteCodex("desktop", now.AddMinutes(-2), 0.4, resetAt: now.AddHours(1));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Equal("desktop", candidates.Fresh?.DeviceName);
        Assert.Same(candidates.Fresh, candidates.Selected);
    }

    [Fact]
    public void Stale_observation_is_used_as_last_resort_while_its_window_is_open()
    {
        var now = Local(24, 12, 0);
        WriteCodex("laptop", now.AddMinutes(-90), 0.6, resetAt: now.AddHours(1));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Null(candidates.Fresh);
        Assert.Equal("laptop", candidates.LastResort?.DeviceName);
        Assert.Same(candidates.LastResort, candidates.Selected);
    }

    [Fact]
    public void Stale_observation_whose_window_reset_is_kept_for_notice_but_not_used()
    {
        var now = Local(24, 12, 0);
        WriteCodex("laptop", now.AddMinutes(-90), 0.99, resetAt: now.AddMinutes(-1));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Equal("laptop", candidates.LastObserved?.DeviceName);
        Assert.Null(candidates.LastResort);
        Assert.Null(candidates.Selected);
    }

    [Fact]
    public void Right_after_midnight_yesterdays_folder_is_searched()
    {
        // 23:40 에 다른 PC 가 올린 값은 어제 날짜 폴더에 있다. 00:10 에 오늘 폴더만 보면 놓친다.
        var now = Local(24, 0, 10);
        WriteCodex("laptop", Local(23, 23, 40), 0.55, resetAt: now.AddHours(2));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Equal("laptop", candidates.LastResort?.DeviceName);
        Assert.Equal(0.55, candidates.Selected!.Quota!.ShortUsagePercent, 6);
    }

    [Fact]
    public void Right_after_midnight_a_fresh_value_in_yesterdays_folder_is_still_fresh()
    {
        var now = Local(24, 0, 1);
        WriteCodex("laptop", Local(23, 23, 59), 0.3, resetAt: now.AddHours(2));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Equal("laptop", candidates.Fresh?.DeviceName);
    }

    [Fact]
    public void Fresh_value_whose_window_just_reset_is_not_selected()
    {
        // 신선도 기준(5분) 안이어도 그 사이 리셋을 지났다면 리셋 전 %라 쓰면 안 된다.
        var now = Local(24, 14, 1);
        WriteCodex("laptop", Local(24, 13, 58), 0.97, resetAt: Local(24, 14, 0));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Null(candidates.Fresh);
        Assert.Null(candidates.Selected);
        Assert.Equal("laptop", candidates.LastObserved?.DeviceName);
    }

    [Fact]
    public void Fresh_value_in_yesterdays_folder_whose_window_reset_at_midnight_is_not_selected()
    {
        var now = Local(24, 0, 1);
        WriteCodex("laptop", Local(23, 23, 59), 0.9, resetAt: Local(24, 0, 0));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Null(candidates.Selected);
    }

    [Fact]
    public void Observation_older_than_a_day_is_ignored_even_in_yesterdays_folder()
    {
        var now = Local(24, 0, 30);
        WriteCodex("laptop", Local(23, 0, 10), 0.8, resetAt: now.AddDays(3));

        var candidates = Select("reader", now, UsageProviderKind.Codex, TimeSpan.FromMinutes(5));

        Assert.Null(candidates.LastObserved);
    }

    [Fact]
    public void Antigravity_last_resort_is_rejected_once_every_model_reset()
    {
        var now = Local(24, 12, 0);
        Write("laptop", now.AddMinutes(-90), UsageProviderKind.Antigravity,
            UsageSyncQuotaWindowTests.AntigravityQuota(now.AddMinutes(-10), now.AddMinutes(-5)));

        var candidates = Select("reader", now, UsageProviderKind.Antigravity, TimeSpan.FromMinutes(5));

        Assert.NotNull(candidates.LastObserved);
        Assert.Null(candidates.Selected);
    }

    [Fact]
    public void OpenCode_last_resort_follows_the_same_rule()
    {
        var now = Local(24, 12, 0);
        Write("laptop", now.AddMinutes(-90), UsageProviderKind.OpenCode,
            UsageSyncQuotaWindowTests.OpenCodeQuota(now.AddHours(1), now.AddDays(2), now.AddDays(10)));

        var candidates = Select("reader", now, UsageProviderKind.OpenCode, TimeSpan.FromMinutes(5));

        Assert.Null(candidates.Fresh);
        Assert.Equal("laptop", candidates.Selected?.DeviceName);
    }

    private UsageSyncQuotaCandidates Select(string device, DateTimeOffset now, string provider, TimeSpan freshTtl)
    {
        var reader = CreateService(device, now);
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var read = reader.ReadSnapshots(SyncRoot, provider, null, today);
        return reader.SelectQuotaCandidates(SyncRoot, provider, null, today, read.Snapshots, freshTtl);
    }

    private void WriteCodex(string device, DateTimeOffset observedAt, double shortPercent, DateTimeOffset resetAt) =>
        Write(device, observedAt, UsageProviderKind.Codex, new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = shortPercent,
            ShortResetAt = resetAt,
        });

    private void Write(string device, DateTimeOffset observedAt, string provider, UsageSyncQuotaSnapshot quota)
    {
        var writer = CreateService(device, observedAt);
        writer.WriteSnapshot(SyncRoot, writer.CreateSnapshot(provider, null, quota, new UsageSyncLocalTotals()));
    }

    private UsageSyncService CreateService(string deviceName, DateTimeOffset now) =>
        new(Path.Combine(_tempRoot, "appdata", deviceName), () => now, deviceName);

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }
}
