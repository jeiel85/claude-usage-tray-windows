using System.Windows;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>시간선 마커(#237) — 예전 열 두 개짜리 Grid 와 같은 자리에 그리는지.</summary>
public class TimelineMarkerTests
{
    [Theory]
    [InlineData(0.5, 98.5, 1.5)]   // 선의 오른쪽 끝이 너비 × 비율
    [InlineData(1.0, 198.5, 1.5)]
    [InlineData(0.005, 0, 1)]      // 왼쪽 끝은 요소 안으로 자른다
    [InlineData(1.7, 198.5, 1.5)]  // 범위 밖은 1 로
    public void LineSitsWhereTheWindowHasReached(double percent, double left, double width)
    {
        var rect = TimelineMarker.LineRect(200, 8, percent);
        Assert.Equal(left, rect.X, 6);
        Assert.Equal(width, rect.Width, 6);
        Assert.Equal(0, rect.Y);
        Assert.Equal(8, rect.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.3)]
    [InlineData(double.NaN)]
    public void NothingIsDrawnAtTheStartOrForAnUnknownValue(double percent)
    {
        // 예전 Grid 도 0* 열에 오른쪽 정렬한 선이라 보이지 않았다.
        Assert.True(TimelineMarker.LineRect(200, 8, percent).IsEmpty);
    }

    [Fact]
    public void NothingIsDrawnBeforeLayout()
    {
        Assert.True(TimelineMarker.LineRect(0, 8, 0.5).IsEmpty);
        Assert.True(TimelineMarker.LineRect(200, 0, 0.5).IsEmpty);
    }
}
