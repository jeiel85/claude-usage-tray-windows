using ClaudeUsageTray.Models;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// 다른 PC 에서 동기화된 할당량을 로컬 API/조회 실패 시 최후 폴백으로 쓸지 판정하는
/// <see cref="MainViewModel.IsSyncedQuotaWindowStillActive"/> 를 단위로 검증한다.
/// 원래 Claude(TrySyncClaudeUsage) 전용으로 추가됐지만, Codex 도 같은 이유(짧은 신선도 기준만으로는
/// 두 PC 가 동시에 조회 실패·백오프에 걸렸을 때 공백이 생김)로 같은 판정을 그대로 재사용한다.
/// 사용량 % 는 창이 끝날 때까지 증가만 하므로, 창이 이미 리셋됐다면
/// 오래된 스냅샷을 보여주는 순간 실제보다 부풀려 보인다 — 이 경계를 놓치면 안 된다.
/// </summary>
public class MainViewModelClaudeSyncTests
{
    private static UsageSyncSnapshot Snapshot(UsageSyncQuotaSnapshot? quota, string provider = UsageProviderKind.Claude) => new()
    {
        Provider = provider,
        DeviceName = "OTHER-PC",
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Quota = quota,
    };

    [Fact]
    public void Null_snapshot_is_not_active()
    {
        Assert.False(MainViewModel.IsSyncedQuotaWindowStillActive(null));
    }

    [Fact]
    public void Snapshot_without_quota_data_is_not_active()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot { HasData = false });
        Assert.False(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
    }

    [Fact]
    public void Quota_with_future_reset_is_still_active()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = 0.21,
            ShortResetAt = DateTimeOffset.UtcNow.AddHours(2),
        });

        Assert.True(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
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
            ShortResetAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });

        Assert.False(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
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
            ShortResetAt = DateTimeOffset.UtcNow.AddHours(2),
            LongUsagePercent = 0.95,
            LongResetAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        });

        Assert.False(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
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

        Assert.True(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
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
            LongResetAt = DateTimeOffset.UtcNow.AddDays(4),
        }, UsageProviderKind.Codex);

        Assert.True(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
    }

    [Fact]
    public void Codex_weekly_only_quota_whose_long_reset_already_passed_is_rejected()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortResetAt = null,
            LongUsagePercent = 0.99,
            LongResetAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        }, UsageProviderKind.Codex);

        Assert.False(MainViewModel.IsSyncedQuotaWindowStillActive(snapshot));
    }
}

/// <summary>
/// "신선한 값 우선, 없으면 당일 관측치 중 창이 유효한 것" 선택 규칙 — Codex·Antigravity 의
/// 최후 폴백이 실제로 신선한 값을 밀어내지 않는지, 신선한 값이 없을 때만 개입하는지 검증한다.
/// </summary>
public class SelectQuotaWithLastResortTests
{
    private static UsageSyncSnapshot Snapshot(double shortPercent, TimeSpan resetIn, string device) => new()
    {
        Provider = UsageProviderKind.Codex,
        DeviceName = device,
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Quota = new UsageSyncQuotaSnapshot
        {
            HasData = true,
            ShortUsagePercent = shortPercent,
            ShortResetAt = DateTimeOffset.UtcNow.Add(resetIn),
        },
    };

    [Fact]
    public void Fresh_value_wins_even_when_last_observed_exists()
    {
        var fresh = Snapshot(0.4, TimeSpan.FromHours(2), "LAPTOP");
        var lastObserved = Snapshot(0.9, TimeSpan.FromHours(2), "LAPTOP");

        Assert.Same(fresh, MainViewModel.SelectQuotaWithLastResort(fresh, lastObserved));
    }

    [Fact]
    public void Stale_last_observed_is_used_when_no_fresh_value_and_window_still_active()
    {
        var lastObserved = Snapshot(0.6, TimeSpan.FromHours(1), "LAPTOP");

        Assert.Same(lastObserved, MainViewModel.SelectQuotaWithLastResort(null, lastObserved));
    }

    [Fact]
    public void Last_observed_is_rejected_once_its_window_has_reset()
    {
        var lastObserved = Snapshot(0.99, TimeSpan.FromMinutes(-1), "LAPTOP");

        Assert.Null(MainViewModel.SelectQuotaWithLastResort(null, lastObserved));
    }

    [Fact]
    public void Both_missing_yields_null()
    {
        Assert.Null(MainViewModel.SelectQuotaWithLastResort(null, null));
    }
}
