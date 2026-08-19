using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class SessionMonitorTests : IDisposable
{
    private readonly string _root;

    public SessionMonitorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cut-session-monitor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void CanInstantiate()
    {
        var monitor = new SessionMonitor();
        Assert.NotNull(monitor);
    }

    [Fact]
    public void ScanTodayUsage_SumsTodayEntries()
    {
        WriteSession("proj-a/a.jsonl",
            AssistantLine(Today(9), input: 10, output: 20, cacheRead: 300, cacheWrite: 40),
            AssistantLine(Today(10), input: 1, output: 2, cacheRead: 3, cacheWrite: 4));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(11, stats.TotalInputTokens);
        Assert.Equal(22, stats.TotalOutputTokens);
        Assert.Equal(303, stats.TotalCacheReadTokens);
        Assert.Equal(44, stats.TotalCacheWriteTokens);
        Assert.Equal(1, stats.SessionCount);
    }

    /// <summary>
    /// 회귀 방지: 증분 스캔 시절엔 두 번째 호출이 "직전 스캔 이후 델타"만 돌려줘서
    /// 오늘 총량이 실제의 일부로 기록됐다. 몇 번을 호출하든 같은 총량이어야 한다.
    /// </summary>
    [Fact]
    public void ScanTodayUsage_IsIdempotent_AcrossRepeatedScans()
    {
        WriteSession("proj-a/a.jsonl",
            AssistantLine(Today(9), input: 10, output: 20, cacheRead: 300, cacheWrite: 40));

        var monitor = new SessionMonitor(_root);
        var first = monitor.ScanTodayUsage();
        var second = monitor.ScanTodayUsage();
        var third = monitor.ScanTodayUsage();

        Assert.Equal(first.GrandTotal, second.GrandTotal);
        Assert.Equal(first.GrandTotal, third.GrandTotal);
        Assert.Equal(370, third.GrandTotal);
    }

    /// <summary>
    /// 회귀 방지: 스캔 사이에 파일이 커지면 새로 붙은 부분만이 아니라 그날 총량이 나와야 한다.
    /// (활성 세션이 계속 기록 중일 때 실제로 사용량이 1/3 수준으로 줄어들던 경로)
    /// </summary>
    [Fact]
    public void ScanTodayUsage_ReturnsDayTotal_WhenFileGrowsBetweenScans()
    {
        var path = WriteSession("proj-a/a.jsonl",
            AssistantLine(Today(9), input: 100, output: 200, cacheRead: 3000, cacheWrite: 400));

        var monitor = new SessionMonitor(_root);
        var before = monitor.ScanTodayUsage();
        Assert.Equal(3700, before.GrandTotal);

        File.AppendAllText(path,
            AssistantLine(Today(11), input: 1, output: 2, cacheRead: 30, cacheWrite: 4) + Environment.NewLine,
            new UTF8Encoding(false));

        var after = monitor.ScanTodayUsage();

        Assert.Equal(3737, after.GrandTotal);
        Assert.Equal(101, after.TotalInputTokens);
    }

    [Fact]
    public void ScanTodayUsage_ExcludesEntriesBeforeToday()
    {
        WriteSession("proj-a/a.jsonl",
            AssistantLine(Today(9).AddDays(-1), input: 999, output: 999, cacheRead: 999, cacheWrite: 999),
            AssistantLine(Today(9), input: 1, output: 2, cacheRead: 3, cacheWrite: 4));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(10, stats.GrandTotal);
    }

    [Fact]
    public void ScanTodayUsage_AggregatesAcrossProjectDirectories()
    {
        WriteSession("proj-a/a.jsonl", AssistantLine(Today(9), input: 1, output: 0, cacheRead: 0, cacheWrite: 0));
        WriteSession("proj-b/nested/b.jsonl", AssistantLine(Today(9), input: 2, output: 0, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(3, stats.TotalInputTokens);
        Assert.Equal(2, stats.SessionCount);
    }

    /// <summary>시간대별 집계는 캐시 토큰까지 포함한다 (증분 경로는 input+output 만 더해 값이 달라졌다).</summary>
    [Fact]
    public void ScanTodayUsage_HourlyTokensIncludeCacheTokens()
    {
        WriteSession("proj-a/a.jsonl",
            AssistantLine(Today(9), input: 1, output: 2, cacheRead: 30, cacheWrite: 4));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(37, stats.HourlyTokens[9]);
        Assert.Equal(0, stats.HourlyTokens.Where((_, h) => h != 9).Sum());
    }

    [Fact]
    public void ScanTodayUsage_IgnoresMalformedLinesAndNonAssistantEntries()
    {
        WriteSession("proj-a/a.jsonl",
            "{ not json",
            "{\"type\":\"user\",\"timestamp\":\"" + Iso(Today(9)) + "\"}",
            AssistantLine(Today(9), input: 5, output: 0, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(5, stats.TotalInputTokens);
        Assert.Equal(1, stats.SessionCount);
    }

    [Fact]
    public void ScanTodayUsage_ReturnsEmpty_WhenRootMissing()
    {
        var stats = new SessionMonitor(Path.Combine(_root, "does-not-exist")).ScanTodayUsage();

        Assert.Equal(0, stats.GrandTotal);
        Assert.Equal(0, stats.SessionCount);
    }

    [Fact]
    public void ScanTodayUsage_CollectsSessionDetails()
    {
        WriteSession("proj-a/06828cf3-231e-48d9-8360-045684782a13.jsonl",
            UserLine(Today(8), "오늘 세션을 리스트로 보여줘"),
            AssistantLine(Today(9), input: 10, output: 20, cacheRead: 300, cacheWrite: 40),
            AssistantLine(Today(11), input: 1, output: 2, cacheRead: 3, cacheWrite: 4));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        var session = Assert.Single(stats.Sessions);
        Assert.Equal("06828cf3-231e-48d9-8360-045684782a13", session.SessionId);
        Assert.Equal(@"D:\Project\demo", session.ProjectPath);
        Assert.Equal("master", session.GitBranch);
        Assert.Equal("오늘 세션을 리스트로 보여줘", session.Title);
        Assert.Equal(380, session.TotalTokens);
        Assert.Equal(new DateTimeOffset(Today(11)).ToUniversalTime().UtcDateTime, session.LastActivityUtc);
    }

    /// <summary>
    /// 슬래시 명령 래퍼(&lt;command-name&gt;…)나 시스템 주입 문구는 세션을 알아보는 데 도움이 안 된다.
    /// 제목은 사람이 실제로 친 첫 프롬프트여야 한다.
    /// </summary>
    [Fact]
    public void ScanTodayUsage_SkipsWrapperAndMetaLines_WhenPickingTitle()
    {
        WriteSession("proj-a/a.jsonl",
            UserLine(Today(8), "<command-name>/clear</command-name>"),
            UserLine(Today(8), "로컬 명령 출력입니다", isMeta: true),
            UserLine(Today(8), "진짜 첫 프롬프트"),
            AssistantLine(Today(9), input: 1, output: 1, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal("진짜 첫 프롬프트", Assert.Single(stats.Sessions).Title);
    }

    [Fact]
    public void ScanTodayUsage_FallsBackToFileName_WhenSessionIdMissing()
    {
        WriteSession("proj-a/legacy-session.jsonl",
            AssistantLine(Today(9), input: 5, output: 0, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        var session = Assert.Single(stats.Sessions);
        Assert.Equal("legacy-session", session.SessionId);
        Assert.Equal("", session.ProjectPath);
        Assert.Equal(5, session.TotalTokens);
    }

    [Fact]
    public void ScanTodayUsage_ExcludesSessionsWithoutTodayActivity()
    {
        WriteSession("proj-a/yesterday.jsonl",
            UserLine(Today(-5), "어제 시작한 세션"),
            AssistantLine(Today(-5), input: 100, output: 100, cacheRead: 0, cacheWrite: 0));
        WriteSession("proj-b/today.jsonl",
            AssistantLine(Today(9), input: 1, output: 1, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(stats.SessionCount, stats.Sessions.Count);
        Assert.Equal("today", Assert.Single(stats.Sessions).SessionId);
    }

    /// <summary>어제 시작해 오늘까지 이어진 세션도 제목(=어제의 첫 프롬프트)으로 알아볼 수 있어야 한다.</summary>
    [Fact]
    public void ScanTodayUsage_KeepsTitleFromEarlierDay_ForContinuedSession()
    {
        WriteSession("proj-a/a.jsonl",
            UserLine(Today(-6), "어제 시작한 작업"),
            AssistantLine(Today(-6), input: 100, output: 100, cacheRead: 0, cacheWrite: 0),
            AssistantLine(Today(9), input: 1, output: 2, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        var session = Assert.Single(stats.Sessions);
        Assert.Equal("어제 시작한 작업", session.Title);
        // 목록의 토큰 수도 "오늘 쓴 만큼"이어야 한다 — 어제 몫이 섞이면 타일 합계와 어긋난다.
        Assert.Equal(3, session.TotalTokens);
    }

    // ---------------------------------------------------------------- helpers

    private static DateTime Today(int hour) => DateTime.Today.AddHours(hour);

    private static string Iso(DateTime local) =>
        new DateTimeOffset(local).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string UserLine(DateTime localTimestamp, string text, bool isMeta = false) =>
        $"{{\"type\":\"user\",\"timestamp\":\"{Iso(localTimestamp)}\"," +
        (isMeta ? "\"isMeta\":true," : "") +
        $"\"cwd\":\"D:\\\\Project\\\\demo\",\"gitBranch\":\"master\"," +
        $"\"sessionId\":\"06828cf3-231e-48d9-8360-045684782a13\"," +
        $"\"message\":{{\"role\":\"user\",\"content\":{System.Text.Json.JsonSerializer.Serialize(text)}}}}}";

    private static string AssistantLine(DateTime localTimestamp, long input, long output, long cacheRead, long cacheWrite) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{Iso(localTimestamp)}\",\"message\":{{\"usage\":{{" +
        $"\"input_tokens\":{input},\"output_tokens\":{output}," +
        $"\"cache_read_input_tokens\":{cacheRead},\"cache_creation_input_tokens\":{cacheWrite}}}}}}}";

    private string WriteSession(string relativePath, params string[] lines)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }
}
