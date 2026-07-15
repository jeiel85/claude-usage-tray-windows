using System;
using System.Globalization;
using System.IO;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class CodexUsageMonitorTests
{
    // 회귀 방지 핵심: rate_limits.secondary 가 null(백엔드 구조 변경) 이어도
    // token_count 라인이 예외로 skip 되지 않고 토큰이 정상 집계되어야 한다.
    // 또한 창이 하나(주간)뿐이면 그 창이 Short 슬롯에 오고 window_minutes 도 채워진다.
    [Fact]
    public void SecondaryNull_StillSumsTokens_AndReadsPrimaryWindow()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "2020", "01", "01");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "rollout-today-a.jsonl");

            var ts = MakeTodayLocalIsoUtc(10, 0);
            File.WriteAllLines(file, new[]
            {
                TokenCountLine(ts, input: 10000, cached: 3000, output: 500, reasoning: 100,
                    total: 10600, primaryPercent: 16.0, primaryWindowMinutes: 10080, secondaryJson: "null"),
            });

            var monitor = new CodexUsageMonitor();
            var snap = monitor.GetTodaySnapshot(root);

            Assert.True(snap.HasData);
            Assert.Equal(10000, snap.TotalInputTokens);
            Assert.Equal(3000, snap.TotalCacheReadTokens);
            Assert.Equal(600, snap.TotalOutputTokens); // output + reasoning_output
            Assert.Equal(0.16, snap.ShortUsagePercent, 3);
            Assert.Equal(10080, snap.ShortWindowMinutes);
            Assert.Equal(0, snap.LongUsagePercent); // secondary=null → 예외 없이 창 없음
            Assert.Null(snap.LongWindowMinutes);
            Assert.Null(snap.LongResetAt);
        }
        finally { Directory.Delete(root, true); }
    }

    // 두 창이 오면 window_minutes 오름차순으로 Short(짧은)/Long(긴) 슬롯에 배치되어야 한다.
    // primary 가 주간(긴 창)이고 secondary 가 5시간(짧은 창)이어도 5시간이 Short 로 온다.
    [Fact]
    public void TwoWindows_AreSortedByLength_ShortestFirst()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "s");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "rollout-today-b.jsonl");

            var ts = MakeTodayLocalIsoUtc(9, 30);
            // primary = 주간(10080분, 37%), secondary = 5시간(300분, 12%)
            var secondary = @"{""used_percent"":12.0,""window_minutes"":300,""resets_at"":1900000000}";
            File.WriteAllLines(file, new[]
            {
                TokenCountLine(ts, input: 5000, cached: 0, output: 50, reasoning: 0,
                    total: 5050, primaryPercent: 37.0, primaryWindowMinutes: 10080, secondaryJson: secondary),
            });

            var monitor = new CodexUsageMonitor();
            var snap = monitor.GetTodaySnapshot(root);

            Assert.True(snap.HasData);
            // 5시간(짧은 창)이 Short, 주간(긴 창)이 Long — 슬롯 순서와 무관하게 길이순 배치
            Assert.Equal(0.12, snap.ShortUsagePercent, 3);
            Assert.Equal(300, snap.ShortWindowMinutes);
            Assert.Equal(0.37, snap.LongUsagePercent, 3);
            Assert.Equal(10080, snap.LongWindowMinutes);
        }
        finally { Directory.Delete(root, true); }
    }

    // 오늘 활동이 없으면 데이터 없음으로 처리되어야 한다.
    [Fact]
    public void NoUsageToday_ReturnsNoData()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "old");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "rollout-old.jsonl");
            File.WriteAllLines(file, new[]
            {
                TokenCountLine("2020-01-01T00:00:00.000Z", input: 999, cached: 0, output: 9, reasoning: 0,
                    total: 1008, primaryPercent: 5.0, primaryWindowMinutes: 10080, secondaryJson: "null"),
            });

            var monitor = new CodexUsageMonitor();
            var snap = monitor.GetTodaySnapshot(root);

            Assert.False(snap.HasData);
            Assert.False(string.IsNullOrEmpty(snap.ErrorMessage));
        }
        finally { Directory.Delete(root, true); }
    }

    // 누적(total_token_usage) 델타가 여러 라인에 걸쳐 정상 합산되는지 확인.
    [Fact]
    public void MultipleLines_SumsDeltasOfCumulativeTotals()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "m");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "rollout-today-c.jsonl");

            var ts1 = MakeTodayLocalIsoUtc(8, 0);
            var ts2 = MakeTodayLocalIsoUtc(8, 5);
            File.WriteAllLines(file, new[]
            {
                // 누적 시작
                TokenCountLine(ts1, input: 1000, cached: 200, output: 100, reasoning: 0,
                    total: 1100, primaryPercent: 4.0, primaryWindowMinutes: 10080, secondaryJson: "null"),
                // 누적 증가 → 델타(input +2000, cached +300, output +50)만 더해져야 함
                TokenCountLine(ts2, input: 3000, cached: 500, output: 150, reasoning: 0,
                    total: 3150, primaryPercent: 8.0, primaryWindowMinutes: 10080, secondaryJson: "null"),
            });

            var monitor = new CodexUsageMonitor();
            var snap = monitor.GetTodaySnapshot(root);

            Assert.True(snap.HasData);
            Assert.Equal(3000, snap.TotalInputTokens);   // 1000 + 2000
            Assert.Equal(500, snap.TotalCacheReadTokens); // 200 + 300
            Assert.Equal(150, snap.TotalOutputTokens);    // 100 + 50
            Assert.Equal(0.08, snap.ShortUsagePercent, 3); // 최신 라인 퍼센트
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TokenCountLine(string ts, long input, long cached, long output,
        long reasoning, long total, double primaryPercent, int primaryWindowMinutes, string secondaryJson)
    {
        var primary = string.Format(CultureInfo.InvariantCulture,
            @"{{""used_percent"":{0},""window_minutes"":{1},""resets_at"":1900000000}}", primaryPercent, primaryWindowMinutes);
        return string.Format(CultureInfo.InvariantCulture,
            @"{{""timestamp"":""{0}"",""type"":""event_msg"",""payload"":{{""type"":""token_count"",""info"":{{""total_token_usage"":{{""input_tokens"":{1},""cached_input_tokens"":{2},""output_tokens"":{3},""reasoning_output_tokens"":{4},""total_tokens"":{5}}}}},""rate_limits"":{{""limit_id"":""codex"",""primary"":{6},""secondary"":{7},""plan_type"":""plus""}}}}}}",
            ts, input, cached, output, reasoning, total, primary, secondaryJson);
    }

    private static string CreateTempCodexRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"codex-monitor-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 오늘의 로컬 시각(시·분)을 ISO 8601 UTC 문자열로 변환 — 파서가 ToLocalTime 후 today.Date로 비교하므로 매칭됨.
    /// </summary>
    private static string MakeTodayLocalIsoUtc(int hour, int minute)
    {
        var local = DateTime.Today.AddHours(hour).AddMinutes(minute);
        return local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
