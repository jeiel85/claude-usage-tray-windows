using System;
using System.IO;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class GeminiCliUsageMonitorTests
{
    [Fact]
    public void ReadsCumulativeTotalTokensFromSessionLogs()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-a.json");
            File.WriteAllLines(sessionFile, new[]
            {
                @"{""total_tokens"": 100}",
                @"{""total_tokens"": 250}"
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            Assert.Equal(250, snapshot.TotalInputTokens);
            Assert.True(snapshot.ShortUsagePercent > 0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ReadsTokenCountWhenCumulativeTotalIsMissing()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp", "nested");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-b.json");
            File.WriteAllLines(sessionFile, new[]
            {
                @"{""token_count"": 40}",
                @"{""tokens"": 60}"
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            Assert.Equal(100, snapshot.TotalInputTokens);
            Assert.Equal(1, snapshot.SessionCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempGeminiRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gemini-monitor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
