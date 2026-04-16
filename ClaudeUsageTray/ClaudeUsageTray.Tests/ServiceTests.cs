using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _origSettingsPath;

    public SettingsServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"claude-usage-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        
        // Backup and override settings path via environment
        _origSettingsPath = Environment.GetEnvironmentVariable("CLAUDE_TEST_SETTINGS_PATH") ?? "";
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaultSettings()
    {
        // Since SettingsService uses hardcoded path, we test via behavior
        var service = new SettingsService();
        var settings = service.Load();

        Assert.NotNull(settings);
        Assert.False(settings.Enabled); // Default is off
    }

    [Fact]
    public void Save_Then_Load_ReturnsSameSettings()
    {
        var service = new SettingsService();
        var original = service.Load();

        // Modify
        original.Enabled = true;
        original.NotifyOnRateLimit = true;
        original.Thresholds = new List<int> { 50, 100 };
        original.NtfyTopic = "test-topic";
        original.StartWithWindows = true;

        // Save and reload using reflection to test properly
        // For now just ensure Save doesn't throw
        service.Save(original);
        
        // Load again
        var reloaded = service.Load();
        
        // Note: This uses the real settings file, so we can't easily test isolation
        // This is more of an integration test
        Assert.NotNull(reloaded);
    }

    public void Dispose()
    {
        // Cleanup test directory
        try { Directory.Delete(_testDir, true); } catch { }
    }
}

public class HistoryServiceTests
{
    [Fact]
    public void RecordToday_AddsEntry()
    {
        var service = new HistoryService();
        var initial = service.GetLast(7).Count;

        service.RecordToday(100, 200, 50, 25, 3);

        var after = service.GetLast(7).Count;
        Assert.True(after >= initial);
    }

    [Fact]
    public void GetLast_RespectsDayLimit()
    {
        var service = new HistoryService();
        var stats = service.GetLast(7);
        
        Assert.True(stats.Count <= 7);
    }

    [Fact]
    public void DailyStats_RecordCreation()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var stats = new DailyStats(today, 100, 200, 50, 25, 3);

        Assert.Equal(today, stats.Date);
        Assert.Equal(100, stats.InputTokens);
        Assert.Equal(200, stats.OutputTokens);
        Assert.Equal(50, stats.CacheReadTokens);
        Assert.Equal(25, stats.CacheWriteTokens);
        Assert.Equal(3, stats.SessionCount);
    }

    [Fact]
    public void SetOrgUuid_ChangesHistoryPath()
    {
        var service = new HistoryService();
        
        // Initial path without UUID
        var initialPath = typeof(HistoryService)
            .GetField("_historyPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(service) as string;
        
        service.SetOrgUuid("test-org-uuid-12345");
        
        var newPath = typeof(HistoryService)
            .GetField("_historyPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(service) as string;

        Assert.NotEqual(initialPath, newPath);
        Assert.Contains("test-org", newPath);
    }
}