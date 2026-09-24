using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class UsageCalculatorTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1.0K")]
    [InlineData(1500, "1.5K")]
    [InlineData(999999, "1000.0K")]
    [InlineData(1000000, "1.0M")]
    [InlineData(1500000, "1.5M")]
    [InlineData(10000000, "10.0M")]
    public void FormatTokenShort_FormatsCorrectly(long tokens, string expected)
    {
        var result = UsageCalculator.FormatTokenShort(tokens);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(50, 2)]
    [InlineData(75, 3)]
    [InlineData(90, 4)]
    [InlineData(100, 5)]
    [InlineData(25, 2)]
    [InlineData(0, 2)]
    public void ThresholdToPriority_ReturnsCorrectPriority(int threshold, int expectedPriority)
    {
        var result = UsageCalculator.ThresholdToPriority(threshold);
        Assert.Equal(expectedPriority, result);
    }

    [Fact]
    public void CalcCostLabel_ReturnsEmpty_WhenCostTooSmall()
    {
        var result = UsageCalculator.CalcCostLabel(0, 0, 0, 0);
        Assert.Equal("", result);
    }

    [Fact]
    public void CalcCostLabel_ReturnsFormatted_WhenCostSignificant()
    {
        var result = UsageCalculator.CalcCostLabel(100000, 50000, 0, 0);
        Assert.Contains("$", result);
    }

    [Fact]
    public void FormatResetLabel_ReturnsEmpty_WhenResetAtIsNull()
    {
        var result = UsageCalculator.FormatResetLabel(null, false, false, DateTimeOffset.Now);
        Assert.Equal("", result);
    }

    [Fact]
    public void FormatResetLabel_ReturnsEmpty_WhenResetAtIsPast()
    {
        var pastTime = DateTimeOffset.Now.AddHours(-1);
        var result = UsageCalculator.FormatResetLabel(pastTime, false, false, DateTimeOffset.Now);
        Assert.Equal("", result);
    }

    [Fact]
    public void FormatResetLabel_ShowsMinutesAndSeconds_WhenLessThan10Minutes()
    {
        var resetAt = DateTimeOffset.Now.AddMinutes(5).AddSeconds(30);
        var result = UsageCalculator.FormatResetLabel(resetAt, false, false, DateTimeOffset.Now);
        Assert.Contains("m", result);
        Assert.Contains("s", result);
    }

    [Fact]
    public void FormatResetLabel_ShowsOnlyMinutes_WhenLessThan1Hour()
    {
        var resetAt = DateTimeOffset.Now.AddMinutes(30);
        var result = UsageCalculator.FormatResetLabel(resetAt, false, false, DateTimeOffset.Now);
        Assert.Contains("m", result);
        Assert.DoesNotContain("h", result);
    }

    [Fact]
    public void FormatResetLabel_ShowsHoursAndMinutes_WhenLessThan1Day()
    {
        var resetAt = DateTimeOffset.Now.AddHours(3).AddMinutes(15);
        var result = UsageCalculator.FormatResetLabel(resetAt, false, false, DateTimeOffset.Now);
        Assert.Contains("h", result);
        Assert.Contains("m", result);
    }

    [Fact]
    public void FormatResetLabel_ShowsDaysAndHours_WhenMoreThan1Day()
    {
        var resetAt = DateTimeOffset.Now.AddDays(2).AddHours(5);
        var result = UsageCalculator.FormatResetLabel(resetAt, false, false, DateTimeOffset.Now);
        Assert.Contains("d", result);
        Assert.Contains("h", result);
    }

    [Fact]
    public void FormatResetLabel_IncludesAbsoluteTime_WhenShowAbsoluteIsTrue()
    {
        var resetAt = DateTimeOffset.Now.AddHours(2);
        var result = UsageCalculator.FormatResetLabel(resetAt, false, true, DateTimeOffset.Now);
        Assert.Contains("(", result);
        Assert.Contains(")", result);
        Assert.Contains(":", result);
    }

    [Fact]
    public void FormatResetLabel_UsesEstimatedFormat_WhenIsEstimatedIsTrue()
    {
        var resetAt = DateTimeOffset.Now.AddHours(2);
        var result = UsageCalculator.FormatResetLabel(resetAt, true, false, DateTimeOffset.Now);
        Assert.True(result.Contains("~") || result.Contains("약") || result.Contains("estimated") || result.Contains("推定"),
            $"Expected estimated format indicator in: {result}");
    }

    [Fact]
    public void CalcDepletionLabel_ReturnsEmpty_WhenUsagePercentTooLow()
    {
        var w = new UsageWindow { Utilization = 1, ResetsAt = DateTimeOffset.Now.AddHours(4).ToString("o") };
        var result = UsageCalculator.CalcDepletionLabel(w, DateTimeOffset.Now);
        Assert.Equal("", result);
    }

    [Fact]
    public void CalcDepletionLabel_ReturnsEmpty_WhenUsagePercentIs100()
    {
        var w = new UsageWindow { Utilization = 100, ResetsAt = DateTimeOffset.Now.AddHours(4).ToString("o") };
        var result = UsageCalculator.CalcDepletionLabel(w, DateTimeOffset.Now);
        Assert.Equal("", result);
    }

    [Fact]
    public void CalcDepletionLabel_ReturnsEmpty_WhenResetsAtIsNull()
    {
        var w = new UsageWindow { Utilization = 50, ResetsAt = null };
        var result = UsageCalculator.CalcDepletionLabel(w, DateTimeOffset.Now);
        Assert.Equal("", result);
    }

    [Fact]
    public void CalcDepletionLabel_ReturnsEmpty_WhenElapsedTooShort()
    {
        var resetAt = DateTimeOffset.Now.AddHours(5).AddMinutes(-2);
        var w = new UsageWindow { Utilization = 50, ResetsAt = resetAt.ToString("o") };
        var result = UsageCalculator.CalcDepletionLabel(w, DateTimeOffset.Now);
        Assert.Equal("", result);
    }

    [Fact]
    public void IsNoUsageInformational_ReturnsFalse_WhenMessageIsNull()
    {
        var result = UsageCalculator.IsNoUsageInformational(null, UsageProviderKind.Codex);
        Assert.False(result);
    }

    [Fact]
    public void IsNoUsageInformational_ReturnsFalse_WhenMessageIsEmpty()
    {
        var result = UsageCalculator.IsNoUsageInformational("", UsageProviderKind.Codex);
        Assert.False(result);
    }

    [Fact]
    public void IsNoUsageInformational_ReturnsFalse_WhenProviderIsClaude()
    {
        var result = UsageCalculator.IsNoUsageInformational("Some message", UsageProviderKind.Claude);
        Assert.False(result);
    }

    [Fact]
    public void FormatObservedAt_ShowsTimeOnly_ForToday()
    {
        var now = new DateTimeOffset(2026, 9, 24, 9, 30, 0, TimeSpan.FromHours(9));
        var observed = new DateTimeOffset(2026, 9, 24, 0, 5, 0, TimeSpan.FromHours(9));

        Assert.Equal("00:05", UsageCalculator.FormatObservedAt(observed, now));
    }

    [Fact]
    public void FormatObservedAt_AddsDate_ForYesterday()
    {
        // 최후 폴백은 24시간 전 관측까지 쓴다 — 어제 23:40 값을 "23:40" 으로만 적으면 방금 값처럼 읽힌다.
        var now = new DateTimeOffset(2026, 9, 24, 0, 10, 0, TimeSpan.FromHours(9));
        var observed = new DateTimeOffset(2026, 9, 23, 23, 40, 0, TimeSpan.FromHours(9));

        var label = UsageCalculator.FormatObservedAt(observed, now);

        // 날짜 구분자는 문화권을 따른다(ko: "09-23", en: "09/23") — FormatResetLabel 과 같은 형식.
        Assert.Equal(observed.ToString("MM/dd HH:mm"), label);
        Assert.StartsWith("09", label);
        Assert.EndsWith("23 23:40", label);
    }

    [Fact]
    public void FormatObservedAt_ComparesDatesInTheViewersOffset()
    {
        // UTC 로는 같은 날(15:00Z)이어도 보는 쪽(+09:00) 기준으로는 다음 날 00:00 이다.
        var now = new DateTimeOffset(2026, 9, 24, 0, 30, 0, TimeSpan.FromHours(9));
        var observed = new DateTimeOffset(2026, 9, 23, 15, 0, 0, TimeSpan.Zero);

        Assert.Equal("00:00", UsageCalculator.FormatObservedAt(observed, now));
    }
}
