using System.Globalization;
using System.IO;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Codex 세션 로그 스캔 캐시(#314): 바뀐 파일만 다시 읽고, 결과는 처음부터 다 읽은 것과 같아야 한다.
/// </summary>
public class CodexLogScanCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-scan-cache-{Guid.NewGuid():N}");

    public CodexLogScanCacheTests() => Directory.CreateDirectory(Path.Combine(_root, "2026", "11", "20"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string FilePath(string name) => Path.Combine(_root, "2026", "11", "20", $"rollout-{name}.jsonl");

    private static string Today(int hour, int minute) =>
        DateTime.Today.AddHours(hour).AddMinutes(minute).ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    private static string Line(string ts, long total, double percent, string? limitId = "codex", string plan = "plus") =>
        string.Format(CultureInfo.InvariantCulture,
            @"{{""timestamp"":""{0}"",""type"":""event_msg"",""payload"":{{""type"":""token_count"",""info"":{{""total_token_usage"":{{""input_tokens"":{1},""cached_input_tokens"":0,""output_tokens"":{2},""reasoning_output_tokens"":0,""total_tokens"":{3}}}}},""rate_limits"":{{""limit_id"":""{4}"",""primary"":{{""used_percent"":{5},""window_minutes"":300,""resets_at"":1900000000}},""secondary"":null,""plan_type"":""{6}""}}}}}}",
            ts, total * 9 / 10, total / 10, total, limitId, percent, plan);

    private void WriteFiles()
    {
        File.WriteAllLines(FilePath("a"), [Line(Today(9, 0), 1000, 10), Line(Today(9, 30), 3000, 20)]);
        File.WriteAllLines(FilePath("b"), [Line(Today(10, 0), 500, 30), Line(Today(10, 5), 900, 35, limitId: "codex_bengalfox")]);
        File.WriteAllLines(FilePath("c"), [Line(Today(8, 0), 2000, 5, plan: "pro")]);
    }

    private static void AssertSameSnapshot(ProviderUsageSnapshot expected, ProviderUsageSnapshot actual)
    {
        Assert.Equal(expected.HasData, actual.HasData);
        Assert.Equal(expected.TotalInputTokens, actual.TotalInputTokens);
        Assert.Equal(expected.TotalOutputTokens, actual.TotalOutputTokens);
        Assert.Equal(expected.TotalCacheReadTokens, actual.TotalCacheReadTokens);
        Assert.Equal(expected.SessionCount, actual.SessionCount);
        Assert.Equal(expected.HourlyTokens, actual.HourlyTokens);
        Assert.Equal(expected.ShortUsagePercent, actual.ShortUsagePercent, 6);
        Assert.Equal(expected.ShortResetAt, actual.ShortResetAt);
        Assert.Equal(expected.ShortWindowMinutes, actual.ShortWindowMinutes);
        Assert.Equal(expected.PlanType, actual.PlanType);
        Assert.Equal(
            expected.AdditionalQuotas.Select(q => (q.LimitId, q.UsagePercent, q.ResetAt)),
            actual.AdditionalQuotas.Select(q => (q.LimitId, q.UsagePercent, q.ResetAt)));
    }

    [Fact]
    public void UnchangedFiles_AreNotReadAgain()
    {
        WriteFiles();
        var monitor = new CodexUsageMonitor();

        var first = monitor.GetTodaySnapshot(_root);
        Assert.Equal(3, monitor.FilesReadLastScan);

        var second = monitor.GetTodaySnapshot(_root);
        Assert.Equal(0, monitor.FilesReadLastScan);
        AssertSameSnapshot(first, second);

        Assert.True(second.HasData);
        Assert.Equal(3, second.SessionCount);
        Assert.Equal(3000 + 900 + 2000, second.HourlyTokens.Sum());   // 파일마다 세션 누적의 마지막 값: a 3000, b 900, c 2000
    }

    [Fact]
    public void AppendedFile_IsReadAgain_AndMatchesAFreshScan()
    {
        WriteFiles();
        var monitor = new CodexUsageMonitor();
        monitor.GetTodaySnapshot(_root);

        File.AppendAllLines(FilePath("a"), [Line(Today(11, 0), 4500, 60, plan: "team")]);
        var cached = monitor.GetTodaySnapshot(_root);

        Assert.Equal(1, monitor.FilesReadLastScan);
        AssertSameSnapshot(new CodexUsageMonitor().GetTodaySnapshot(_root), cached);
        Assert.Equal(0.60, cached.ShortUsagePercent, 6);   // 덧붙은 줄이 가장 최근 한도
        Assert.Equal("team", cached.PlanType);
    }

    [Fact]
    public void RemovedFile_DropsOutOfTheTotals()
    {
        WriteFiles();
        var monitor = new CodexUsageMonitor();
        monitor.GetTodaySnapshot(_root);

        File.Delete(FilePath("c"));
        var cached = monitor.GetTodaySnapshot(_root);

        Assert.Equal(0, monitor.FilesReadLastScan);
        AssertSameSnapshot(new CodexUsageMonitor().GetTodaySnapshot(_root), cached);
        Assert.Equal(2, cached.SessionCount);
    }

    [Fact]
    public void NewDay_ReadsEveryFileAgain()
    {
        WriteFiles();
        var monitor = new CodexUsageMonitor();
        monitor.GetTodaySnapshot(_root, DateTimeOffset.Now);

        var tomorrow = monitor.GetTodaySnapshot(_root, DateTimeOffset.Now.AddDays(1));

        Assert.Equal(3, monitor.FilesReadLastScan);
        Assert.False(tomorrow.HasData);
        Assert.Equal(0, tomorrow.HourlyTokens.Sum());
    }
}
