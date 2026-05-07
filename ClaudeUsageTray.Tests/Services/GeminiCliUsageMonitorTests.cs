using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class GeminiCliUsageMonitorTests
{
    [Fact]
    public void SumsAllTokenTypesAcrossGeminiLines_UsingMessageTimestamp()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-a.jsonl");

            var ts1 = MakeTodayLocalIsoUtc(10, 15);
            var ts2 = MakeTodayLocalIsoUtc(14, 30);

            File.WriteAllLines(sessionFile, new[]
            {
                // 세션 헤더 — 토큰 없음
                @"{""sessionId"":""sess-a"",""kind"":""main""}",
                // 사용자 입력 — 토큰 없음
                $@"{{""id"":""u1"",""timestamp"":""{ts1}"",""type"":""user"",""content"":[{{""text"":""hi""}}]}}",
                // gemini 응답 #1 — 입력 + 출력 + cached + thoughts 모두 합산
                $@"{{""id"":""g1"",""timestamp"":""{ts1}"",""type"":""gemini"",""tokens"":{{""input"":10000,""output"":80,""cached"":2000,""thoughts"":20,""tool"":0,""total"":12100}},""model"":""gemini-3-flash-preview""}}",
                // gemini 응답 #2 — 다른 시간대
                $@"{{""id"":""g2"",""timestamp"":""{ts2}"",""type"":""gemini"",""tokens"":{{""input"":20000,""output"":120,""cached"":5000,""thoughts"":50,""tool"":0,""total"":25170}},""model"":""gemini-3-flash-preview""}}",
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            // input = 10000 + 20000
            Assert.Equal(30000, snapshot.TotalInputTokens);
            // output = (80+20) + (120+50)  ← thoughts 합산
            Assert.Equal(270, snapshot.TotalOutputTokens);
            // cached read = 2000 + 5000
            Assert.Equal(7000, snapshot.TotalCacheReadTokens);
            // cache write 개념 없음
            Assert.Equal(0, snapshot.TotalCacheWriteTokens);
            Assert.Equal(2, snapshot.RequestCount);
            Assert.Equal(1, snapshot.SessionCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IgnoresUserLinesAndPastDayLinesAndUnparseable()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp", "nested");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-b.jsonl");

            var todayTs = MakeTodayLocalIsoUtc(11, 0);
            var pastTs = "2020-01-01T00:00:00.000Z";

            File.WriteAllLines(sessionFile, new[]
            {
                @"{""sessionId"":""sess-b"",""kind"":""main""}",
                @"{""$set"":{""lastUpdated"":""2020-01-01T00:00:00Z""}}",
                // 과거 날짜 — 무시되어야 함
                $@"{{""id"":""g0"",""timestamp"":""{pastTs}"",""type"":""gemini"",""tokens"":{{""input"":99999,""output"":99999,""total"":199998}},""model"":""x""}}",
                // 사용자 라인 — 무시되어야 함 (type != gemini)
                $@"{{""id"":""u1"",""timestamp"":""{todayTs}"",""type"":""user"",""content"":[]}}",
                // 손상된 JSON — 무시되어야 함
                @"{this is not valid json",
                // 정상 오늘 gemini 라인 1개만 집계되어야 함
                $@"{{""id"":""g1"",""timestamp"":""{todayTs}"",""type"":""gemini"",""tokens"":{{""input"":5000,""output"":50,""cached"":0,""thoughts"":0,""tool"":0,""total"":5050}}}}",
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            Assert.Equal(5000, snapshot.TotalInputTokens);
            Assert.Equal(50, snapshot.TotalOutputTokens);
            Assert.Equal(0, snapshot.TotalCacheReadTokens);
            Assert.Equal(1, snapshot.RequestCount);
            Assert.Equal(1, snapshot.SessionCount);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ReturnsNoUsageWhenNothingMatchesToday()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp");
            Directory.CreateDirectory(tmpDir);
            // 어제 활동도 없는 옛날 파일
            var oldFile = Path.Combine(tmpDir, "session-old.jsonl");
            File.WriteAllLines(oldFile, new[]
            {
                @"{""sessionId"":""old"",""kind"":""main""}",
                @"{""id"":""g"",""timestamp"":""2020-01-01T00:00:00Z"",""type"":""gemini"",""tokens"":{""input"":1,""output"":1,""total"":2}}"
            });
            // 후보 단계에서 mtime 필터링까지 통과하도록 mtime을 인위적으로 과거로 — 그러나 메시지 ts도 과거라 어차피 0
            File.SetLastWriteTime(oldFile, DateTime.Now.AddDays(-30));

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.False(snapshot.HasData);
            Assert.Equal(0, snapshot.RequestCount);
            // "오늘 사용 기록 없음" 메시지가 정보성으로 채워짐
            Assert.False(string.IsNullOrEmpty(snapshot.ErrorMessage));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void HourlyTokensUseMessageTimestampNotFileMtime()
    {
        var root = CreateTempGeminiRoot();
        try
        {
            var tmpDir = Path.Combine(root, "tmp");
            Directory.CreateDirectory(tmpDir);
            var sessionFile = Path.Combine(tmpDir, "session-h.jsonl");

            // 같은 파일 안에 서로 다른 시간대 메시지 두 개 — 시간대별 분리되어야 함
            var t9  = MakeTodayLocalIsoUtc(9, 5);
            var t15 = MakeTodayLocalIsoUtc(15, 47);

            File.WriteAllLines(sessionFile, new[]
            {
                @"{""sessionId"":""h"",""kind"":""main""}",
                $@"{{""id"":""g1"",""timestamp"":""{t9}"",""type"":""gemini"",""tokens"":{{""input"":1000,""output"":100,""total"":1100}}}}",
                $@"{{""id"":""g2"",""timestamp"":""{t15}"",""type"":""gemini"",""tokens"":{{""input"":2000,""output"":200,""total"":2200}}}}",
            });

            var monitor = new GeminiCliUsageMonitor(root);
            var snapshot = monitor.GetTodaySnapshot();

            Assert.True(snapshot.HasData);
            Assert.Equal(1100, snapshot.HourlyTokens[9]);
            Assert.Equal(2200, snapshot.HourlyTokens[15]);
            // 다른 시간대는 0이어야 함
            Assert.Equal(0, snapshot.HourlyTokens.Take(9).Sum());
            Assert.Equal(0, snapshot.HourlyTokens.Skip(10).Take(5).Sum());
            Assert.Equal(0, snapshot.HourlyTokens.Skip(16).Sum());
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

    /// <summary>
    /// 오늘의 로컬 시각(시·분 지정)을 ISO 8601 UTC 문자열로 변환 — 파서가 ToLocalTime 후 today.Date로 비교하므로 매칭됨.
    /// </summary>
    private static string MakeTodayLocalIsoUtc(int hour, int minute)
    {
        var local = DateTime.Today.AddHours(hour).AddMinutes(minute);
        return local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
