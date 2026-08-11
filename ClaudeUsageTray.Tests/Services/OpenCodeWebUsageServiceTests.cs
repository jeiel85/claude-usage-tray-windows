using ClaudeUsageTray.Services;
using System.IO;
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
        Assert.Equal(now.ToUniversalTime(), usage.ObservedAtUtc);
        Assert.Equal(now.AddSeconds(16930), usage.Rolling.ResetAt);
        Assert.Equal(now.AddSeconds(594907), usage.Weekly.ResetAt);
        Assert.Equal(now.AddSeconds(2676664), usage.Monthly.ResetAt);
    }

    [Fact]
    public void CachedFallback_RemainsAvailableDuringRetryButNotPastItsSafetyBounds()
    {
        var observedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(9));
        var usage = new ClaudeUsageTray.Models.OpenCodeWebUsage
        {
            ObservedAtUtc = observedAt,
            Rolling = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddHours(5) },
            Weekly = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddDays(7) },
            Monthly = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddMonths(1) },
        };

        Assert.True(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, observedAt.AddMinutes(39)));
        Assert.False(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, observedAt.AddMinutes(41)));

        var resetExpired = new ClaudeUsageTray.Models.OpenCodeWebUsage
        {
            ObservedAtUtc = observedAt,
            Rolling = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddMinutes(10) },
        };
        Assert.False(OpenCodeWebUsageService.IsCachedFallbackUsable(resetExpired, observedAt.AddMinutes(11)));
        Assert.False(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, observedAt.AddMinutes(-6)));
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

    [Theory]
    [InlineData("https://opencode.ai/workspace/wrk_ABC123", "https://opencode.ai/workspace/wrk_ABC123/go")]
    [InlineData("https://opencode.ai/workspace/wrk_ABC123/go", "https://opencode.ai/workspace/wrk_ABC123/go")]
    [InlineData("https://opencode.ai/workspace/wrk_ABC123/go/", "https://opencode.ai/workspace/wrk_ABC123/go")]
    public void NormalizeWorkspaceGoUri_AcceptsOnlyWorkspaceRoutes(string input, string expected)
    {
        Assert.Equal(new Uri(expected), OpenCodeWebUsageService.NormalizeWorkspaceGoUri(new Uri(input)));
    }

    [Theory]
    [InlineData("https://opencode.ai/workspace")]
    [InlineData("https://opencode.ai/workspace/wrk_ABC123/settings")]
    [InlineData("http://opencode.ai/workspace/wrk_ABC123/go")]
    [InlineData("https://evil.example/workspace/wrk_ABC123/go")]
    public void NormalizeWorkspaceGoUri_RejectsUnsafeOrMissingRoutes(string input)
    {
        Assert.Null(OpenCodeWebUsageService.NormalizeWorkspaceGoUri(new Uri(input)));
    }

    [Fact]
    public void WorkspaceRoute_PersistsAcrossServiceRestarts_AndFallsBackToAuthWhenMissing()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"opencode-route-{Guid.NewGuid():N}");
        try
        {
            using (var first = new OpenCodeWebUsageService(dataRoot))
            {
                Assert.Equal(new Uri("https://opencode.ai/auth"), first.NavigationStartUri);
                Assert.True(OpenCodeWebUsageService.WriteWorkspaceRoute(
                    Path.Combine(dataRoot, "workspace-route.txt"),
                    new Uri("https://opencode.ai/workspace/wrk_TEST123/go")));
            }

            using var restarted = new OpenCodeWebUsageService(dataRoot);
            Assert.Equal(new Uri("https://opencode.ai/workspace/wrk_TEST123/go"), restarted.NavigationStartUri);
            Assert.True(restarted.HasSavedSession);
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
        }
    }

    [Fact]
    public void HasSavedSession_IsFalseUntilAReadSucceeds()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"opencode-route-{Guid.NewGuid():N}");
        try
        {
            using var service = new OpenCodeWebUsageService(dataRoot);
            Assert.False(service.HasSavedSession);
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
        }
    }

    // 부팅 직후 네트워크 미준비처럼 확인 자체를 못 한 실패는 곧 회복되므로 짧게 쉬었다가 다시 본다.
    // 30분을 쉬면 멀쩡한 세션에 로그인 버튼이 그만큼 붙어 있게 된다.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(9, 16)]
    public void RetryDelay_ForUnavailable_BacksOffGraduallyAndStaysShorterThanSignedOut(
        int consecutiveFailures, int expectedMinutes)
    {
        var delay = OpenCodeWebUsageService.RetryDelayFor(
            ClaudeUsageTray.Models.OpenCodeWebSessionState.Unavailable, consecutiveFailures);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
        Assert.True(delay < OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.SignedOut, 1));
    }

    // 로그인이 풀린 상태는 사용자가 로그인하기 전엔 결과가 달라지지 않으므로 종전대로 길게 쉰다.
    [Fact]
    public void RetryDelay_ForSignedOut_StaysLong()
    {
        Assert.Equal(TimeSpan.FromMinutes(30),
            OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.SignedOut, 1));
        Assert.Equal(TimeSpan.FromMinutes(30),
            OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.SignedOut, 9));
    }
}
