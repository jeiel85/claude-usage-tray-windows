using System;
using System.IO;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class GeminiCliUsageMonitorTests
{
    [Fact]
    public void SumsOutputTokensAcrossRequests()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-a.jsonl");
            File.WriteAllLines(sessionFile, new[]
            {
                @"{""tokens"":{""input"":10000,""output"":80,""total"":10080}}",
                @"{""tokens"":{""input"":20000,""output"":120,""total"":20120}}"
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            Assert.Equal(200, snapshot.TotalOutputTokens);
            Assert.Equal(2, snapshot.RequestCount);
            Assert.Equal(0, snapshot.ShortUsagePercent);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IgnoresLinesWithoutTokensOutputField()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp", "nested");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-b.jsonl");
            File.WriteAllLines(sessionFile, new[]
            {
                @"{""sessionId"":""abc"",""kind"":""main""}",
                @"{""$set"":{""lastUpdated"":""2026-01-01T00:00:00Z""}}",
                @"{""tokens"":{""input"":5000,""output"":50,""total"":5050}}"
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            Assert.Equal(50, snapshot.TotalOutputTokens);
            Assert.Equal(1, snapshot.RequestCount);
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
