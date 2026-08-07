using System;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 시간선(진행 막대 위 시간 진행률 마커) 위치 계산 회귀 테스트.
///
/// 회귀 1 (v1.36.2): Codex 창이 주간(10080분)인데 5시간으로 역산하면 진행률이 음수 → 0 으로 잘려
/// 마커가 막대 왼쪽 끝에 붙어 보이지 않았다.
/// 회귀 2 (v1.38.0): 리셋이 이미 지난 값을 1 로 잘라서, 오늘 요청이 없는 PC 에서 며칠 전 로그의
/// 끝난 창이 "100% 경과"로 표시돼 마커가 오른쪽 끝에 박혔다. 이제 창 밖이면 null(마커 숨김)이다.
/// </summary>
public class UsageCalculatorTimeProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 13, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void TimeProgress_ReturnsNull_WhenResetUnknown()
    {
        Assert.Null(UsageCalculator.TimeProgress(null, TimeSpan.FromHours(5), Now));
    }

    [Fact]
    public void TimeProgress_ReturnsNull_WhenWindowLengthIsNotPositive()
    {
        Assert.Null(UsageCalculator.TimeProgress(Now.AddHours(2), TimeSpan.Zero, Now));
    }

    [Theory]
    [InlineData(5.0, 0.0)]   // 리셋까지 5시간 남음 = 창 시작 시점
    [InlineData(2.5, 0.5)]   // 절반 경과
    [InlineData(0.0, 1.0)]   // 리셋 시점
    public void TimeProgress_MapsRemainingTimeToElapsedRatio(double hoursLeft, double expected)
    {
        var progress = UsageCalculator.TimeProgress(
            Now.AddHours(hoursLeft), TimeSpan.FromHours(5), Now);

        Assert.NotNull(progress);
        Assert.Equal(expected, progress!.Value, 6);
    }

    // 회귀: 리셋이 이미 지난 창은 위치를 지어내지 않는다.
    // 예전에는 1 로 잘려 마커가 오른쪽 끝에 고정됐다 — 오늘 Codex 요청이 없는 PC 에서 보이던 증상.
    [Fact]
    public void TimeProgress_ReturnsNull_WhenResetAlreadyPassed()
    {
        Assert.Null(UsageCalculator.TimeProgress(Now.AddHours(-1), TimeSpan.FromHours(5), Now));
        Assert.Null(UsageCalculator.TimeProgress(Now.AddSeconds(-1), TimeSpan.FromHours(5), Now));
        // 1970-01-01 (resets_at: 0 을 그대로 받은 경우) 도 같은 처리.
        Assert.Null(UsageCalculator.TimeProgress(DateTimeOffset.UnixEpoch, TimeSpan.FromHours(5), Now));
    }

    // 리셋이 창 길이보다 멀리 있으면 창 길이가 어긋난 것 — 0(왼쪽 끝)으로 단정하지 않는다.
    [Fact]
    public void TimeProgress_ReturnsNull_WhenResetIsFartherAwayThanOneWindow()
    {
        Assert.Null(UsageCalculator.TimeProgress(Now.AddHours(9), TimeSpan.FromHours(5), Now));
    }

    // 회귀: 주간 창(10080분)을 5시간으로 계산하면 시간선이 사라진다.
    [Fact]
    public void TimeProgress_WeeklyWindow_IsVisible_OnlyWithRealWindowLength()
    {
        var resetAt = Now.AddHours(56); // 주간 창에서 2일 8시간 남음 → 약 66.7% 경과
        var weekly = UsageCalculator.WindowSpan(10080, TimeSpan.FromHours(5));

        var correct = UsageCalculator.TimeProgress(resetAt, weekly, Now);
        var hardcodedFiveHour = UsageCalculator.TimeProgress(resetAt, TimeSpan.FromHours(5), Now);

        Assert.NotNull(correct);
        Assert.Equal(1.0 - 56.0 / 168.0, correct!.Value, 6);
        Assert.Null(hardcodedFiveHour); // 과거 버그 재현 — 창 길이를 틀리면 위치를 알 수 없다
    }

    [Theory]
    [InlineData(10080, 10080)] // 주간
    [InlineData(300, 300)]     // 5시간
    public void WindowSpan_UsesReportedWindowMinutes(int minutes, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes),
            UsageCalculator.WindowSpan(minutes, TimeSpan.FromHours(5)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-10)]
    public void WindowSpan_FallsBack_WhenWindowLengthIsUnknownOrInvalid(int? minutes)
    {
        var fallback = TimeSpan.FromDays(7);
        Assert.Equal(fallback, UsageCalculator.WindowSpan(minutes, fallback));
    }
}
