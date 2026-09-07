using ClaudeUsageTray.Models;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

/// <summary>
/// 다른 PC 에서 동기화된 Claude 할당량을 로컬 API 실패 시 최후 폴백으로 쓸지 판정하는
/// <see cref="MainViewModel.ClaudeQuotaWindowStillActive"/> 만 단위로 검증한다.
/// 사용량 % 는 5시간 창이 끝날 때까지 증가만 하므로, 창이 이미 리셋됐다면
/// 오래된 스냅샷을 보여주는 순간 실제보다 부풀려 보인다 — 이 경계를 놓치면 안 된다.
/// </summary>
public class MainViewModelClaudeSyncTests
{
    private static UsageSyncSnapshot Snapshot(UsageSyncQuotaSnapshot? quota) => new()
    {
        Provider = UsageProviderKind.Claude,
        DeviceName = "OTHER-PC",
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Quota = quota,
    };

    [Fact]
    public void Null_snapshot_is_not_active()
    {
        Assert.False(MainViewModel.ClaudeQuotaWindowStillActive(null));
    }

    [Fact]
    public void Snapshot_without_quota_data_is_not_active()
    {
        var snapshot = Snapshot(new UsageSyncQuotaSnapshot { HasData = false });
        Assert.False(MainViewModel.ClaudeQuotaWindowStillActive(snapshot));
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

        Assert.True(MainViewModel.ClaudeQuotaWindowStillActive(snapshot));
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

        Assert.False(MainViewModel.ClaudeQuotaWindowStillActive(snapshot));
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

        Assert.False(MainViewModel.ClaudeQuotaWindowStillActive(snapshot));
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

        Assert.True(MainViewModel.ClaudeQuotaWindowStillActive(snapshot));
    }
}
