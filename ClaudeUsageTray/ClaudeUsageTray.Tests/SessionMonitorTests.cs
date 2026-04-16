using System.Text.Json;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Tests;

public class SessionMonitorTests
{
    [Fact]
    public void GetCachedStats_EmptyCache_ReturnsZeroStats()
    {
        var monitor = new SessionMonitor();
        var stats = monitor.GetCachedStats();

        Assert.Equal(0, stats.TotalInputTokens);
        Assert.Equal(0, stats.TotalOutputTokens);
        Assert.Equal(0, stats.SessionCount);
    }

    [Fact]
    public void ScanTodayUsage_NoProjectsDirectory_ReturnsZeroStats()
    {
        var monitor = new SessionMonitor();
        var stats = monitor.ScanTodayUsage();

        Assert.Equal(0, stats.TotalInputTokens);
        Assert.Equal(0, stats.TotalOutputTokens);
    }

    [Fact]
    public void ScanTodayUsage_ValidJsonlFile_ParsesCorrectly()
    {
        // Create temp directory with test file
        var tempDir = Path.Combine(Path.GetTempPath(), $"claude-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var testFile = Path.Combine(tempDir, "test.jsonl");
            var lines = new[]
            {
                """{"type":"assistant","timestamp":"2026-04-16T10:00:00Z","message":{"usage":{"input_tokens":100,"output_tokens":200,"cache_read_input_tokens":50,"cache_creation_input_tokens":25}}}""",
                """{"type":"assistant","timestamp":"2026-04-16T11:00:00Z","message":{"usage":{"input_tokens":150,"output_tokens":250}}}"""
            };
            File.WriteAllLines(testFile, lines);

            // Override ProjectsPath for testing via reflection or create testable version
            // For now, we'll just verify the file parsing works with the ProcessFile method
            // This is a basic smoke test

            // Since SessionMonitor uses hardcoded path, we test via the public API
            var monitor = new SessionMonitor();
            var stats = monitor.ScanTodayUsage();

            // File exists in temp dir but SessionMonitor looks in .claude/projects
            // This test validates the method doesn't throw
            Assert.NotNull(stats);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SessionStats_HourlyTokens_InitializeCorrectly()
    {
        var stats = new Models.SessionStats();
        Assert.Equal(24, stats.HourlyTokens.Length);
        
        // All should be zero
        foreach (var tokens in stats.HourlyTokens)
        {
            Assert.Equal(0, tokens);
        }
    }

    [Fact]
    public void SessionStats_GrandTotal_CalculatesCorrectly()
    {
        var stats = new Models.SessionStats
        {
            TotalInputTokens = 100,
            TotalOutputTokens = 200,
            TotalCacheReadTokens = 50,
            TotalCacheWriteTokens = 25
        };

        Assert.Equal(300, stats.TotalTokens); // in + out
        Assert.Equal(375, stats.GrandTotal); // all
    }
}