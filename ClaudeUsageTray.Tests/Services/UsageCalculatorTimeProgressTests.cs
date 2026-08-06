using System;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 시간선(진행 막대 위 시간 진행률 마커) 위치 계산 회귀 테스트.
/// 핵심 회귀: Codex 창이 주간(10080분)인데 5시간으로 역산하면 진행률이 음수 → 0 으로 잘려
/// 마커가 막대 왼쪽 끝에 붙어 보이지 않았다.
/// </summary>
public class UsageCalculatorTimeProgressTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 13, 0, 0, TimeSpan.FromHours(9));

    [Fact]
    public void TimeProgress_ReturnsZero_WhenResetUnknown()
    {
        Assert.Equal(0, UsageCalculator.TimeProgress(null, TimeSpan.FromHours(5), Now));
    }

    [Fact]
    public void TimeProgress_ReturnsZero_WhenWindowLengthIsNotPositive()
    {
        Assert.Equal(0, UsageCalculator.TimeProgress(Now.AddHours(2), TimeSpan.Zero, Now));
    }

    [Theory]
    [InlineData(5.0, 0.0)]   // 리셋까지 5시간 남음 = 창 시작 시점
    [InlineData(2.5, 0.5)]   // 절반 경과
    [InlineData(0.0, 1.0)]   // 리셋 시점
    public void TimeProgress_MapsRemainingTimeToElapsedRatio(double hoursLeft, double expected)
    {
        var progress = UsageCalculator.TimeProgress(
            Now.AddHours(hoursLeft), TimeSpan.FromHours(5), Now);

        Assert.Equal(expected, progress, 6);
    }

    [Fact]
    public void TimeProgress_ClampsToRange_WhenResetIsOutsideWindow()
    {
        // 리셋이 이미 지남 → 1 로 고정
        Assert.Equal(1.0, UsageCalculator.TimeProgress(Now.AddHours(-1), TimeSpan.FromHours(5), Now), 6);
        // 리셋이 창 길이보다 더 멀리 있음 → 0 으로 고정
        Assert.Equal(0.0, UsageCalculator.TimeProgress(Now.AddHours(9), TimeSpan.FromHours(5), Now), 6);
    }

    // 회귀: 주간 창(10080분)을 5시간으로 계산하면 0 으로 잘려 시간선이 사라진다.
    [Fact]
    public void TimeProgress_WeeklyWindow_IsVisible_OnlyWithRealWindowLength()
    {
        var resetAt = Now.AddHours(56); // 주간 창에서 2일 8시간 남음 → 약 66.7% 경과
        var weekly = UsageCalculator.WindowSpan(10080, TimeSpan.FromHours(5));

        var correct = UsageCalculator.TimeProgress(resetAt, weekly, Now);
        var hardcodedFiveHour = UsageCalculator.TimeProgress(resetAt, TimeSpan.FromHours(5), Now);

        Assert.Equal(1.0 - 56.0 / 168.0, correct, 6);
        Assert.Equal(0.0, hardcodedFiveHour, 6); // 과거 버그 재현 — 마커가 왼쪽 끝에 숨음
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
