using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
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

    /// <summary>
    /// 토큰 집계 없이 rate_limits 만 있는 줄(info: null) — 한도별 줄을 흉내 낸다.
    /// <paramref name="limitId"/> 가 null 이면 limit_id 필드 자체를 빼서 그 필드가 없던 버전의 로그를 만든다.
    /// </summary>
    private static string RateLimitsOnlyLine(string ts, string? limitId, double primaryPercent, int primaryWindowMinutes,
        long? primaryResetsAt = 1900000000)
    {
        var resetsAtField = primaryResetsAt is null ? "" : string.Format(CultureInfo.InvariantCulture, @",""resets_at"":{0}", primaryResetsAt);
        var primary = string.Format(CultureInfo.InvariantCulture,
            @"{{""used_percent"":{0},""window_minutes"":{1}{2}}}", primaryPercent, primaryWindowMinutes, resetsAtField);
        var limitIdField = limitId is null ? "" : $@"""limit_id"":""{limitId}"",""limit_name"":null,";
        return string.Format(CultureInfo.InvariantCulture,
            @"{{""timestamp"":""{0}"",""type"":""event_msg"",""payload"":{{""type"":""token_count"",""info"":null,""rate_limits"":{{{1}""primary"":{2},""secondary"":null}}}}}}",
            ts, limitIdField, primary);
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

    // 오늘 Codex 를 쓰지 않아 세션 로그에 rate_limits 가 없어도, 로그인 파일의 id_token 으로
    // 요금제 배지를 채울 수 있어야 한다.
    [Fact]
    public void TryReadPlanTypeFromAuth_ReadsPlanFromIdTokenClaims()
    {
        var authPath = Path.Combine(CreateTempCodexRoot(), "auth.json");
        WriteAuthFile(authPath, """{"chatgpt_plan_type":"plus","chatgpt_account_id":"acc"}""");

        Assert.Equal("plus", CodexUsageMonitor.TryReadPlanTypeFromAuth(authPath));
    }

    [Fact]
    public void TryReadPlanTypeFromAuth_ReturnsNullWhenClaimMissing()
    {
        var authPath = Path.Combine(CreateTempCodexRoot(), "auth.json");
        WriteAuthFile(authPath, """{"chatgpt_account_id":"acc"}""");

        Assert.Null(CodexUsageMonitor.TryReadPlanTypeFromAuth(authPath));
    }

    [Theory]
    [InlineData("not-a-jwt")]
    [InlineData("header.%%%.signature")]
    [InlineData("")]
    public void TryReadPlanTypeFromAuth_SurvivesMalformedToken(string idToken)
    {
        var authPath = Path.Combine(CreateTempCodexRoot(), "auth.json");
        File.WriteAllText(authPath, "{\"tokens\":{\"id_token\":\"" + idToken + "\"}}");

        Assert.Null(CodexUsageMonitor.TryReadPlanTypeFromAuth(authPath));
    }

    [Fact]
    public void TryReadPlanTypeFromAuth_ReturnsNullWhenFileMissing()
        => Assert.Null(CodexUsageMonitor.TryReadPlanTypeFromAuth(
            Path.Combine(Path.GetTempPath(), $"codex-missing-{Guid.NewGuid():N}", "auth.json")));

    // 회귀 방지(#148): auth.json 의 access_token/account_id 는 최상위가 아니라 tokens 아래에 있다.
    // 실제 파일(2026-09-18 실측)에는 auth_mode·OPENAI_API_KEY·last_refresh 등 다른 최상위 필드도
    // 섞여 있으므로, 이들이 있어도 tokens 아래 값을 정확히 골라내는지 함께 확인한다.
    [Fact]
    public void TryGetApiCredentials_ReadsFromNestedTokensObject()
    {
        var authPath = Path.Combine(CreateTempCodexRoot(), "auth.json");
        File.WriteAllText(authPath, """
        {
            "auth_mode": "chatgpt",
            "OPENAI_API_KEY": null,
            "tokens": {
                "id_token": "header.payload.signature",
                "access_token": "test-access-token",
                "refresh_token": "test-refresh-token",
                "account_id": "test-account-id"
            },
            "last_refresh": "2026-09-18T00:00:00.000000000Z"
        }
        """);

        var (token, accountId) = CodexUsageMonitor.TryGetApiCredentials(authPath);

        Assert.Equal("test-access-token", token);
        Assert.Equal("test-account-id", accountId);
    }

    [Fact]
    public void TryGetApiCredentials_ReturnsNullWhenTopLevelAccessTokenOnly()
    {
        // 예전 버그가 읽으려던(그러나 실제 파일에는 없는) 모양 — 최상위에 access_token 만 있는 경우.
        var authPath = Path.Combine(CreateTempCodexRoot(), "auth.json");
        File.WriteAllText(authPath, """{"access_token":"should-not-be-read"}""");

        var (token, accountId) = CodexUsageMonitor.TryGetApiCredentials(authPath);

        Assert.Null(token);
        Assert.Null(accountId);
    }

    [Fact]
    public void TryGetApiCredentials_ReturnsNullWhenFileMissing()
    {
        var (token, accountId) = CodexUsageMonitor.TryGetApiCredentials(
            Path.Combine(Path.GetTempPath(), $"codex-missing-{Guid.NewGuid():N}", "auth.json"));

        Assert.Null(token);
        Assert.Null(accountId);
    }

    // 회귀 방지: Direct API(/backend-api/wham/usage) 응답 스키마는 로컬 세션 로그와 완전히 다르다 —
    // 창이 rate_limit.primary_window/secondary_window 로 한 겹 더 감싸이고, 필드명도
    // limit_window_seconds(초)/reset_at 으로 다르다(로그는 window_minutes(분)/resets_at).
    // 실제 openai/codex 저장소의 OpenAPI 모델(RateLimitWindowSnapshot 등, 2026-09-18 실측) 기준.
    [Fact]
    public void ParseDirectApiUsage_ReadsRealWhamUsageSchema()
    {
        var resetAt = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds();
        var longResetAt = DateTimeOffset.UtcNow.AddDays(5).ToUnixTimeSeconds();
        using var doc = JsonDocument.Parse($$"""
        {
            "plan_type": "plus",
            "rate_limit": {
                "allowed": true,
                "limit_reached": false,
                "primary_window": {
                    "used_percent": 42,
                    "limit_window_seconds": 18000,
                    "reset_after_seconds": 10800,
                    "reset_at": {{resetAt}}
                },
                "secondary_window": {
                    "used_percent": 8,
                    "limit_window_seconds": 604800,
                    "reset_after_seconds": 432000,
                    "reset_at": {{longResetAt}}
                }
            },
            "account_id": "acc-1",
            "user_id": "user-1"
        }
        """);

        var snapshot = CodexUsageMonitor.ParseDirectApiUsage(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.HasData);
        Assert.Equal("Direct API", snapshot.DataSource);
        Assert.Equal("plus", snapshot.PlanType);
        Assert.Equal(0.42, snapshot.ShortUsagePercent, 3);
        Assert.Equal(300, snapshot.ShortWindowMinutes); // 18000s / 60 = 5h
        Assert.Equal(0.08, snapshot.LongUsagePercent, 3);
        Assert.Equal(10080, snapshot.LongWindowMinutes); // 604800s / 60 = 7d
        Assert.Empty(snapshot.AdditionalQuotas); // additional_rate_limits 가 없으면 추가 게이지도 없다
    }

    // #182: 이미지 생성 같은 추가 한도는 응답 최상위 additional_rate_limits 에 따로 온다
    // (openai/codex codex-backend-openapi-models 의 AdditionalRateLimitDetails). 창마다 한 줄로 펴되
    // 한도 안에서는 짧은 창이 먼저, 기본 한도(codex)가 다시 실려 오면 두 번 그리지 않고, 이미 리셋된 창은 버린다.
    [Fact]
    public void ParseDirectApiUsage_ReadsAdditionalRateLimits()
    {
        var now = DateTimeOffset.UtcNow;
        var fiveHourReset = now.AddHours(2).ToUnixTimeSeconds();
        var weeklyReset = now.AddDays(3).ToUnixTimeSeconds();
        var expiredReset = now.AddMinutes(-5).ToUnixTimeSeconds();
        using var doc = JsonDocument.Parse($$"""
        {
            "plan_type": "plus",
            "rate_limit": {
                "primary_window": { "used_percent": 10, "limit_window_seconds": 18000, "reset_after_seconds": 100, "reset_at": {{fiveHourReset}} }
            },
            "additional_rate_limits": [
                {
                    "limit_name": "image_gen",
                    "metered_feature": "codex_image_gen",
                    "rate_limit": {
                        "allowed": true,
                        "limit_reached": false,
                        "primary_window": { "used_percent": 70, "limit_window_seconds": 604800, "reset_after_seconds": 100, "reset_at": {{weeklyReset}} },
                        "secondary_window": { "used_percent": 25, "limit_window_seconds": 18000, "reset_after_seconds": 100, "reset_at": {{fiveHourReset}} }
                    }
                },
                {
                    "limit_name": "codex",
                    "metered_feature": "codex",
                    "rate_limit": { "primary_window": { "used_percent": 99, "limit_window_seconds": 18000, "reset_after_seconds": 100, "reset_at": {{fiveHourReset}} } }
                },
                {
                    "limit_name": "gpt-5.3-codex-spark",
                    "metered_feature": "codex_bengalfox",
                    "rate_limit": { "primary_window": { "used_percent": 50, "limit_window_seconds": 18000, "reset_after_seconds": 0, "reset_at": {{expiredReset}} } }
                },
                "not-an-object"
            ]
        }
        """);

        var snapshot = CodexUsageMonitor.ParseDirectApiUsage(doc.RootElement, now);

        Assert.NotNull(snapshot);
        Assert.Equal(0.10, snapshot!.ShortUsagePercent, 3); // 기본 게이지는 그대로
        Assert.Collection(snapshot.AdditionalQuotas,
            fiveHour =>
            {
                Assert.Equal("codex_image_gen", fiveHour.LimitId);
                Assert.Equal("image_gen", fiveHour.LimitName);
                Assert.Equal(300, fiveHour.WindowMinutes);
                Assert.Equal(0.25, fiveHour.UsagePercent, 3);
            },
            weekly =>
            {
                Assert.Equal("codex_image_gen", weekly.LimitId);
                Assert.Equal(10080, weekly.WindowMinutes);
                Assert.Equal(0.70, weekly.UsagePercent, 3);
                Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(weeklyReset), weekly.ResetAt);
            });
        // Direct API 값의 관측 시각은 조회 시각이다 — 다른 PC 의 로그 값과 겨룰 때 쓴다.
        Assert.All(snapshot.AdditionalQuotas, window => Assert.Equal(now, window.ObservedAt));
    }

    // #182: 최신 CLI 는 한도마다 rate_limits 줄을 따로 남기고 limit_id 로 구분한다. limit_id 를 안 보면
    // 더 늦게 찍힌 추가 한도 줄이 기본 게이지를 덮어써 5시간 창 자리에 이미지 한도 값이 뜬다.
    // 같은 한도의 줄이 여럿이면 가장 늦은 줄을 쓴다.
    [Fact]
    public void LocalLog_AdditionalLimitLine_DoesNotOverwriteMainGauge()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "s");
            Directory.CreateDirectory(dir);
            var latestImageTs = MakeTodayLocalIsoUtc(9, 30);
            File.WriteAllLines(Path.Combine(dir, "rollout-today-limits.jsonl"), new[]
            {
                RateLimitsOnlyLine(MakeTodayLocalIsoUtc(8, 0), "codex_image_gen", primaryPercent: 30.0, primaryWindowMinutes: 10080),
                TokenCountLine(MakeTodayLocalIsoUtc(9, 0), input: 1000, cached: 0, output: 10, reasoning: 0,
                    total: 1010, primaryPercent: 12.0, primaryWindowMinutes: 300, secondaryJson: "null"),
                RateLimitsOnlyLine(latestImageTs, "codex_image_gen", primaryPercent: 80.0, primaryWindowMinutes: 10080),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root);

            Assert.Equal(0.12, snap.ShortUsagePercent, 3);
            Assert.Equal(300, snap.ShortWindowMinutes);
            var image = Assert.Single(snap.AdditionalQuotas);
            Assert.Equal("codex_image_gen", image.LimitId);
            Assert.Equal("", image.LimitName); // 로그 줄의 limit_name 은 null 일 수 있다
            Assert.Equal(0.80, image.UsagePercent, 3);
            Assert.Equal(10080, image.WindowMinutes);
            // 다른 PC 값과 겨룰 때 쓰는 관측 시각은 그 줄의 기록 시각이다.
            Assert.Equal(DateTimeOffset.Parse(latestImageTs, CultureInfo.InvariantCulture), image.ObservedAt);
        }
        finally { Directory.Delete(root, true); }
    }

    // 리셋 시각이 없는 추가 한도 창은 지금 창인지 가릴 수 없어 버린다. 그래서 창이 하나도 남지 않는 더 늦은 줄이
    // 앞서 읽은 정상 값을 지워서도 안 된다.
    [Fact]
    public void LocalLog_AdditionalLimit_IgnoresWindowsWithoutReset_AndKeepsTheLastUsableLine()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "s");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "rollout-today-noreset.jsonl"), new[]
            {
                RateLimitsOnlyLine(MakeTodayLocalIsoUtc(8, 0), "codex_image_gen", primaryPercent: 30.0, primaryWindowMinutes: 10080),
                RateLimitsOnlyLine(MakeTodayLocalIsoUtc(9, 0), "codex_image_gen", primaryPercent: 99.0, primaryWindowMinutes: 10080,
                    primaryResetsAt: null),
                RateLimitsOnlyLine(MakeTodayLocalIsoUtc(9, 0), "codex_other", primaryPercent: 50.0, primaryWindowMinutes: 300,
                    primaryResetsAt: null),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root);

            var image = Assert.Single(snap.AdditionalQuotas);
            Assert.Equal("codex_image_gen", image.LimitId);
            Assert.Equal(0.30, image.UsagePercent, 3);
        }
        finally { Directory.Delete(root, true); }
    }

    // limit_id 가 생기기 전 버전의 로그(필드 없음)는 기본 한도로 읽어야 한다 — 추가 한도로 빠지면 기본 게이지가 빈다.
    [Fact]
    public void LocalLog_LineWithoutLimitId_IsTheMainLimit()
    {
        var root = CreateTempCodexRoot();
        try
        {
            var dir = Path.Combine(root, "s");
            Directory.CreateDirectory(dir);
            File.WriteAllLines(Path.Combine(dir, "rollout-today-legacy.jsonl"), new[]
            {
                RateLimitsOnlyLine(MakeTodayLocalIsoUtc(9, 0), limitId: null, primaryPercent: 44.0, primaryWindowMinutes: 300),
            });

            var snap = new CodexUsageMonitor().GetTodaySnapshot(root);

            Assert.Equal(0.44, snap.ShortUsagePercent, 3);
            Assert.Empty(snap.AdditionalQuotas);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ParseDirectApiUsage_ReturnsNullWhenRateLimitMissing()
    {
        using var doc = JsonDocument.Parse("""{"plan_type":"plus"}""");

        Assert.Null(CodexUsageMonitor.ParseDirectApiUsage(doc.RootElement, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ParseDirectApiUsage_FreePlan_IsNotAPaidSubscription()
    {
        using var doc = JsonDocument.Parse($$"""
        {
            "plan_type": "free",
            "rate_limit": {
                "primary_window": {
                    "used_percent": 5,
                    "limit_window_seconds": 18000,
                    "reset_after_seconds": 100,
                    "reset_at": {{DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()}}
                }
            }
        }
        """);

        var snapshot = CodexUsageMonitor.ParseDirectApiUsage(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.NotNull(snapshot);
        Assert.Equal("free", snapshot!.PlanType);
        Assert.False(MainViewModel.IsPaidCodexSubscription(snapshot.PlanType));
    }

    /// <summary>서명 없는 표시용 id_token 을 만들어 auth.json 형태로 저장한다(payload 만 base64url).</summary>
    private static void WriteAuthFile(string authPath, string authClaimJson)
    {
        var payload = "{\"https://api.openai.com/auth\":" + authClaimJson + "}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        File.WriteAllText(authPath, "{\"tokens\":{\"id_token\":\"header." + encoded + ".signature\"}}");
    }
}
