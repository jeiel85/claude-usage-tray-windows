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
    [Fact]
    public void SecondaryNull_StillSumsTokens_AndReadsPrimaryPercent()
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
                    total: 10600, primaryPercent: 16.0, secondaryJson: "null"),
            });

            var monitor = new CodexUsageMonitor();
            var snap = monitor.GetTodaySnapshot(root);

            Assert.True(snap.HasData);
            Assert.Equal(10000, snap.TotalInputTokens);
            Assert.Equal(3000, snap.TotalCacheReadTokens);
            Assert.Equal(600, snap.TotalOutputTokens); // output + reasoning_output
            Assert.Equal(0.16, snap.ShortUsagePercent, 3);
            Assert.Equal(0, snap.LongUsagePercent); // secondary=null → 예외 없이 0
            Assert.Null(snap.LongResetAt);
        }
        finally { Directory.Delete(root, true); }
    }

    // secondary 가 정상 객체이면 장기 윈도우 퍼센트/리셋도 읽혀야 한다(정상 경로 회귀 방지).
    [Fact]
    public void SecondaryObject_ReadsLongWindowPercent()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "s");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "rollout-today-b.jsonl");

            var ts = MakeTodayLocalIsoUtc(9, 30);
            var secondary = @"{""used_percent"":6.0,""window_minutes"":10080,""resets_at"":1900000000}";
            File.WriteAllLines(file, new[]
            {
                TokenCountLine(ts, input: 5000, cached: 0, output: 50, reasoning: 0,
                    total: 5050, primaryPercent: 16.0, secondaryJson: secondary),
            });

            var monitor = new CodexUsageMonitor();
            var snap = monitor.GetTodaySnapshot(root);

            Assert.True(snap.HasData);
            Assert.Equal(0.16, snap.ShortUsagePercent, 3);
            Assert.Equal(0.06, snap.LongUsagePercent, 3);
            Assert.NotNull(snap.LongResetAt);
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
                    total: 1008, primaryPercent: 5.0, secondaryJson: "null"),
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
                    total: 1100, primaryPercent: 4.0, secondaryJson: "null"),
                // 누적 증가 → 델타(input +2000, cached +300, output +50)만 더해져야 함
                TokenCountLine(ts2, input: 3000, cached: 500, output: 150, reasoning: 0,
                    total: 3150, primaryPercent: 8.0, secondaryJson: "null"),
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
        long reasoning, long total, double primaryPercent, string secondaryJson)
    {
        var primary = string.Format(CultureInfo.InvariantCulture,
            @"{{""used_percent"":{0},""window_minutes"":10080,""resets_at"":1900000000}}", primaryPercent);
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
