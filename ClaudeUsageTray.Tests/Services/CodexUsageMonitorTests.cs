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

    // 회귀(v1.38.0): 오늘 요청이 없는 PC 에서 Codex 시간선이 막대 오른쪽 끝에 박히던 문제.
    // 로그 스캔은 날짜와 무관하게 가장 최근의 rate_limits 를 집어오므로, 며칠 전 세션의
    // "이미 끝난 창"이 그대로 표시됐다. 리셋이 과거면 진행률이 1 로 잘려 마커가 오른쪽 끝에 선다.
    [Fact]
    public void ExpiredWindowInOldLog_IsDropped_SoTimelineHasNoPosition()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "old");
            Directory.CreateDirectory(dir);
            var expired = DateTimeOffset.Now.AddDays(-3).ToUnixTimeSeconds();
            File.WriteAllLines(Path.Combine(dir, "rollout-old.jsonl"), new[]
            {
                TokenCountLine("2026-08-03T01:00:00.000Z", input: 999, cached: 0, output: 9, reasoning: 0,
                    total: 1008, primaryPercent: 61.0, primaryWindowMinutes: 10080, secondaryJson: "null",
                    primaryResetsAt: expired),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root);

            Assert.Null(snap.ShortResetAt);
            Assert.Null(snap.ShortWindowMinutes);
            Assert.Equal(0, snap.ShortUsagePercent);
            Assert.Null(UsageCalculator.TimeProgress(
                snap.ShortResetAt,
                UsageCalculator.WindowSpan(snap.ShortWindowMinutes, TimeSpan.FromHours(5)),
                DateTimeOffset.Now));
        }
        finally { Directory.Delete(root, true); }
    }

    // 아직 열려 있는 창은 오늘 활동이 없어도 유지된다 — 위 규칙이 멀쩡한 데이터까지 지우면 안 된다.
    [Fact]
    public void LiveWindowInOldLog_IsKept()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "old");
            Directory.CreateDirectory(dir);
            var live = DateTimeOffset.Now.AddHours(2).ToUnixTimeSeconds();
            File.WriteAllLines(Path.Combine(dir, "rollout-old.jsonl"), new[]
            {
                TokenCountLine("2026-08-03T01:00:00.000Z", input: 999, cached: 0, output: 9, reasoning: 0,
                    total: 1008, primaryPercent: 61.0, primaryWindowMinutes: 300, secondaryJson: "null",
                    primaryResetsAt: live),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root);

            Assert.Equal(0.61, snap.ShortUsagePercent, 3);
            Assert.Equal(300, snap.ShortWindowMinutes);
            Assert.NotNull(snap.ShortResetAt);
        }
        finally { Directory.Delete(root, true); }
    }

    // resets_at 이 0 이면 "리셋 시각 없음"이다. 그대로 받으면 1970-01-01 이 되어
    // 시간선이 100% 경과로 계산되고 막대 오른쪽 끝에 박힌다. 0 은 무시하고 추정 폴백을 태워야 한다.
    [Fact]
    public void ZeroResetsAt_IsTreatedAsMissing_NotUnixEpoch()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "z");
            Directory.CreateDirectory(dir);
            var todayTen = DateTime.Today.AddHours(10);
            File.WriteAllLines(Path.Combine(dir, "rollout-today-z.jsonl"), new[]
            {
                TokenCountLine(
                    todayTen.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    input: 100, cached: 0, output: 10, reasoning: 0,
                    total: 110, primaryPercent: 20.0, primaryWindowMinutes: 300, secondaryJson: "null",
                    primaryResetsAt: 0),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root, new DateTimeOffset(todayTen.AddHours(1)));

            Assert.True(snap.HasData);
            Assert.NotEqual(DateTimeOffset.UnixEpoch, snap.ShortResetAt);
            // 0 을 무시했으므로 "오늘 첫 활동 + 창 길이" 추정치가 남는다.
            Assert.True(snap.IsShortResetEstimated);
            Assert.Equal(new DateTimeOffset(todayTen).AddMinutes(300), snap.ShortResetAt);
        }
        finally { Directory.Delete(root, true); }
    }

    // 리셋 시각이 없을 때의 추정치는 "오늘 첫 활동 + 창 길이"여야 한다.
    // 날짜를 안 보면 며칠 전 첫 줄이 잡혀 추정 리셋이 과거로 계산되고, 그 값도 시간선을 오른쪽 끝에 박는다.
    [Fact]
    public void EstimatedReset_AnchorsToFirstActivityOfToday_NotOlderLines()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "e");
            Directory.CreateDirectory(dir);
            var todayNine = DateTime.Today.AddHours(9);
            File.WriteAllLines(Path.Combine(dir, "rollout-mixed.jsonl"), new[]
            {
                // 며칠 전 줄이 파일 앞에 온다 — 예전에는 이 시각이 추정 기준이 됐다.
                TokenCountLineWithoutResetTime("2026-08-01T00:00:00.000Z", input: 10, output: 1, total: 11,
                    primaryWindowMinutes: 300),
                TokenCountLineWithoutResetTime(
                    todayNine.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    input: 200, output: 20, total: 220, primaryWindowMinutes: 300),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root, new DateTimeOffset(todayNine.AddHours(1)));

            Assert.True(snap.IsShortResetEstimated);
            Assert.Equal(new DateTimeOffset(todayNine).AddMinutes(300), snap.ShortResetAt);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string TokenCountLine(string ts, long input, long cached, long output,
        long reasoning, long total, double primaryPercent, int primaryWindowMinutes, string secondaryJson,
        long primaryResetsAt = 1900000000)
    {
        var primary = string.Format(CultureInfo.InvariantCulture,
            @"{{""used_percent"":{0},""window_minutes"":{1},""resets_at"":{2}}}", primaryPercent, primaryWindowMinutes, primaryResetsAt);
        return string.Format(CultureInfo.InvariantCulture,
            @"{{""timestamp"":""{0}"",""type"":""event_msg"",""payload"":{{""type"":""token_count"",""info"":{{""total_token_usage"":{{""input_tokens"":{1},""cached_input_tokens"":{2},""output_tokens"":{3},""reasoning_output_tokens"":{4},""total_tokens"":{5}}}}},""rate_limits"":{{""limit_id"":""codex"",""primary"":{6},""secondary"":{7},""plan_type"":""plus""}}}}}}",
            ts, input, cached, output, reasoning, total, primary, secondaryJson);
    }

    /// <summary>resets_at 이 아예 없는 라인 — 리셋 추정 폴백 경로를 태운다.</summary>
    private static string TokenCountLineWithoutResetTime(string ts, long input, long output, long total,
        int primaryWindowMinutes)
    {
        var primary = string.Format(CultureInfo.InvariantCulture,
            @"{{""used_percent"":5.0,""window_minutes"":{0}}}", primaryWindowMinutes);
        return string.Format(CultureInfo.InvariantCulture,
            @"{{""timestamp"":""{0}"",""type"":""event_msg"",""payload"":{{""type"":""token_count"",""info"":{{""total_token_usage"":{{""input_tokens"":{1},""cached_input_tokens"":0,""output_tokens"":{2},""reasoning_output_tokens"":0,""total_tokens"":{3}}}}},""rate_limits"":{{""limit_id"":""codex"",""primary"":{4},""secondary"":null,""plan_type"":""plus""}}}}}}",
            ts, input, output, total, primary);
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
