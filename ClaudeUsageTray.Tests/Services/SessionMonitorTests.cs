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

    [Theory]
    [InlineData(null, "")]
    [InlineData("   \n\t ", "")]
    [InlineData("  첫 줄\r\n  둘째   줄  ", "첫 줄 둘째 줄")]
    [InlineData("  <command-name>/clear</command-name>", "")]
    [InlineData("<local-command-stdout>ok</local-command-stdout>", "")]
    [InlineData("<system-reminder>\nx\n</system-reminder>", "")]
    [InlineData("<ide_opened_file>The user opened a.cs</ide_opened_file>", "")]
    [InlineData("<task-notification attr=\"1\">done</task-notification>", "")]
    // 사용자가 붙여 넣은 HTML 로 시작하는 프롬프트는 제목으로 쓴다(#145).
    [InlineData("<div>버튼이 안 눌려요</div>", "<div>버튼이 안 눌려요</div>")]
    [InlineData("<!DOCTYPE html> 이 페이지 고쳐줘", "<!DOCTYPE html> 이 페이지 고쳐줘")]
    [InlineData("<T> 제네릭 질문", "<T> 제네릭 질문")]
    [InlineData("<-- 화살표", "<-- 화살표")]
    public void NormalizeTitle_FoldsWhitespaceAndSkipsWrappers(string? text, string expected)
    {
        Assert.Equal(expected, SessionMonitor.NormalizeTitle(text));
    }

    [Fact]
    public void NormalizeTitle_KeepsExactlyMaxLength_WithoutEllipsis()
    {
        var eighty = new string('가', 80);

        Assert.Equal(eighty, SessionMonitor.NormalizeTitle(eighty + "   \n"));
    }

    /// <summary>붙여 넣은 긴 로그로 시작한 세션도 제목은 80자 + … 이다(원문 전체를 접지 않고 앞부분만 읽는다).</summary>
    [Fact]
    public void NormalizeTitle_TruncatesVeryLongPrompt()
    {
        var huge = string.Concat(Enumerable.Repeat("로그 한 줄\n", 200_000));

        var title = SessionMonitor.NormalizeTitle(huge);

        Assert.InRange(title.Length, 2, 81);
        Assert.EndsWith("…", title);
        Assert.DoesNotContain("\n", title);
        Assert.StartsWith("로그 한 줄 로그 한 줄", title);
    }

    [Fact]
    public void NormalizeTitle_DoesNotSplitSurrogatePair()
    {
        var text = new string('a', 79) + "😀 끝";

        var title = SessionMonitor.NormalizeTitle(text);

        Assert.Equal(new string('a', 79) + "…", title);
    }

    // ---------------------------------------------------------------- helpers

    // Codex 리뷰봇 지적(#176): 재시작하면 같은 레이트 리밋 기록을 다시 읽는다. 알림 키를 그 시각에 묶으려면
    // 스캔이 기록 자체의 시각을 돌려줘야 한다(감지한 "지금" 이 아니라).
    [Fact]
    public void ScanTodayUsage_RecordsTheTimeOfTheRateLimitEntry()
    {
        var hit = Today(9).AddMinutes(17);
        WriteSession("proj-a/a.jsonl",
            AssistantLine(Today(8), input: 1, output: 1, cacheRead: 0, cacheWrite: 0),
            RateLimitLine(hit));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.True(stats.HasRateLimitHit);
        Assert.Equal(new DateTimeOffset(hit).ToUniversalTime(), stats.RateLimitAt);
    }

    // 스캔을 몇 번 하든(=앱을 몇 번 다시 켜든) 같은 기록에서는 같은 시각이 나와야 키가 흔들리지 않는다.
    [Fact]
    public void ScanTodayUsage_RateLimitTime_IsStableAcrossRepeatedScans()
    {
        WriteSession("proj-a/a.jsonl", RateLimitLine(Today(9)));

        var first = new SessionMonitor(_root).ScanTodayUsage().RateLimitAt;
        var second = new SessionMonitor(_root).ScanTodayUsage().RateLimitAt;

        Assert.NotNull(first);
        Assert.Equal(first, second);
    }

    // 하루에 여러 번 걸렸다면 가장 최근 기록이 이긴다(파일·줄 순서와 무관).
    [Fact]
    public void ScanTodayUsage_RateLimitTime_IsTheLatestEntryAcrossFiles()
    {
        WriteSession("proj-a/a.jsonl", RateLimitLine(Today(11)));
        WriteSession("proj-b/b.jsonl", RateLimitLine(Today(9)), RateLimitLine(Today(10)));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(new DateTimeOffset(Today(11)).ToUniversalTime(), stats.RateLimitAt);
    }

    [Fact]
    public void ScanTodayUsage_RateLimitTime_IsNullWhenThereWasNoRateLimit()
    {
        WriteSession("proj-a/a.jsonl", AssistantLine(Today(9), input: 1, output: 1, cacheRead: 0, cacheWrite: 0));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.False(stats.HasRateLimitHit);
        Assert.Null(stats.RateLimitAt);
    }

    // #332: 서브에이전트 기록(<세션>/subagents/agent-*.jsonl)은 부모의 sessionId 를 싣는다. 파일마다 세면 세션 수가 부풀고
    // 목록에 같은 세션이 겹쳤다. 세션은 하나로, 토큰은 모두 더한다.
    [Fact]
    public void SubagentTranscripts_JoinTheirParentSession()
    {
        const string parent = "11111111-1111-1111-1111-111111111111";
        const string other = "22222222-2222-2222-2222-222222222222";
        WriteSession($"proj-a/{parent}.jsonl",
            SessionLine("user", Today(9), parent, sidechain: false, cwd: "/work/demo", text: "Fix the login bug"),
            SessionLine("assistant", Today(9), parent, sidechain: false, tokens: 100));
        WriteSession($"proj-a/{parent}/subagents/agent-a1.jsonl",
            SessionLine("user", Today(10), parent, sidechain: true, cwd: "/work/demo/sub", text: "Search the repo for X"),
            SessionLine("assistant", Today(11), parent, sidechain: true, tokens: 40));
        WriteSession($"proj-a/{parent}/subagents/agent-a2.jsonl",
            SessionLine("assistant", Today(10), parent, sidechain: true, tokens: 2));
        WriteSession($"proj-b/{other}.jsonl",
            SessionLine("assistant", Today(8), other, sidechain: false, tokens: 7));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(2, stats.SessionCount);
        Assert.Equal(2, stats.Sessions.Count);
        Assert.Equal(149, stats.TotalInputTokens);
        var merged = stats.Sessions.Single(x => x.SessionId == parent);
        Assert.Equal(142, merged.TotalTokens);
        Assert.Equal("Fix the login bug", merged.Title);           // 부모의 첫 프롬프트, 서브에이전트 지시문이 아니다
        Assert.Equal("/work/demo", merged.ProjectPath);
        Assert.Equal(Today(11).ToUniversalTime(), merged.LastActivityUtc); // 가장 늦은 활동은 서브에이전트 쪽
    }

    [Fact]
    public void SubagentOnlyActiveToday_StillCountsItsSessionOnce()
    {
        // 부모 기록이 오늘 안 바뀌어도(서브에이전트만 돈 경우) 세션은 하나로 보이고 서브에이전트 정보로 채운다.
        const string parent = "33333333-3333-3333-3333-333333333333";
        WriteSession($"proj-a/{parent}/subagents/agent-b1.jsonl",
            SessionLine("user", Today(10), parent, sidechain: true, cwd: "/work/demo", text: "Look for Y"),
            SessionLine("assistant", Today(10), parent, sidechain: true, tokens: 5));
        WriteSession($"proj-a/{parent}/subagents/agent-b2.jsonl",
            SessionLine("assistant", Today(11), parent, sidechain: true, tokens: 6));

        var stats = new SessionMonitor(_root).ScanTodayUsage();

        Assert.Equal(1, stats.SessionCount);
        var only = Assert.Single(stats.Sessions);
        Assert.Equal(11, only.TotalTokens);
        Assert.Equal("/work/demo", only.ProjectPath);
    }

    private static string SessionLine(string type, DateTime localTimestamp, string sessionId, bool sidechain,
        string cwd = "", string text = "", long tokens = 0) =>
        $"{{\"type\":\"{type}\",\"timestamp\":\"{Iso(localTimestamp)}\",\"sessionId\":\"{sessionId}\"," +
        $"\"isSidechain\":{(sidechain ? "true" : "false")}," +
        (cwd.Length > 0 ? $"\"cwd\":\"{cwd}\"," : "") +
        (type == "user"
            ? $"\"message\":{{\"role\":\"user\",\"content\":{System.Text.Json.JsonSerializer.Serialize(text)}}}}}"
            : $"\"message\":{{\"usage\":{{\"input_tokens\":{tokens},\"output_tokens\":0," +
              "\"cache_read_input_tokens\":0,\"cache_creation_input_tokens\":0}}}");

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

    private static string RateLimitLine(DateTime localTimestamp) =>
        $"{{\"type\":\"assistant\",\"timestamp\":\"{Iso(localTimestamp)}\",\"error\":\"rate_limit\",\"message\":{{\"usage\":{{" +
        $"\"input_tokens\":0,\"output_tokens\":0,\"cache_read_input_tokens\":0,\"cache_creation_input_tokens\":0}}," +
        "\"content\":[{\"type\":\"text\",\"text\":\"You've hit your limit - resets 3pm\"}]}}";

    private string WriteSession(string relativePath, params string[] lines)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }
}
