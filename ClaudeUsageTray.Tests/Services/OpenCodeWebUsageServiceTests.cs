using ClaudeUsageTray.Services;
using System.Windows;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public sealed class OpenCodeWebUsageServiceTests
{
    [Fact]
    public void ParseUsage_ReadsOfficialWindowsFromHydrationPayload()
    {
        var now = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(9));
        const string html = """
            <script>self.$R=self.$R||[];
            $R[28]($R[18],$R[31]={mine:!0,useBalance:!1,
            rollingUsage:$R[33]={status:"ok",resetInSec:16930,usagePercent:12.5},
            weeklyUsage:$R[34]={status:"ok",resetInSec:594907,usagePercent:42},
            monthlyUsage:$R[35]={status:"ok",resetInSec:2676664,usagePercent:87}});
            </script>
            """;

        var usage = OpenCodeWebUsageService.ParseUsage(html, now);

        Assert.NotNull(usage);
        Assert.Equal(0.125, usage.Rolling.UsagePercent, 6);
        Assert.Equal(0.42, usage.Weekly.UsagePercent, 6);
        Assert.Equal(0.87, usage.Monthly.UsagePercent, 6);
        Assert.Equal(now.AddSeconds(16930), usage.Rolling.ResetAt);
        Assert.Equal(now.AddSeconds(594907), usage.Weekly.ResetAt);
        Assert.Equal(now.AddSeconds(2676664), usage.Monthly.ResetAt);
    }

    [Fact]
    public void ParseUsage_RejectsPartialOrOutOfRangePayloads()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(OpenCodeWebUsageService.ParseUsage(
            "rollingUsage:$R[1]={resetInSec:1,usagePercent:101}", now));
        Assert.Null(OpenCodeWebUsageService.ParseUsage(
            "rollingUsage:$R[1]={resetInSec:1,usagePercent:1}", now));
    }

    [Fact]
    public void CenterWithinWorkArea_UsesExplicitCoordinatesOnNegativeMonitor()
    {
        var bounds = OpenCodeWebUsageService.CenterWithinWorkArea(
            new Rect(-1920, 0, 1920, 1040), new System.Windows.Size(920, 720));

        Assert.Equal(-1420, bounds.Left);
        Assert.Equal(160, bounds.Top);
        Assert.Equal(920, bounds.Width);
        Assert.Equal(720, bounds.Height);
    }

    [Fact]
    public void CenterWithinWorkArea_ClampsWindowToSmallWorkArea()
    {
        var bounds = OpenCodeWebUsageService.CenterWithinWorkArea(
            new Rect(100, 50, 800, 600), new System.Windows.Size(920, 720));

        Assert.Equal(new Rect(100, 50, 800, 600), bounds);
    }
}
