using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 응답·자격 증명 파싱과, 요청에 어떤 클라이언트 식별자를 붙이는지만 검증한다.
/// 서버 동작 자체는 비공식 API 라 테스트로 고정하지 않는다.
/// <see cref="RealSummaryResponse"/> 는 실제 retrieveUserQuotaSummary 응답에서 가져왔다.
/// </summary>
public sealed class AntigravityUsageMonitorTests
{
    private const string RealSummaryResponse = """
    {
      "groups": [
        {
          "buckets": [
            { "bucketId": "gemini-weekly", "displayName": "Weekly Limit Remaining", "window": "weekly",
              "resetTime": "2026-08-18T08:04:36Z", "remainingFraction": 1 },
            { "bucketId": "gemini-5h", "displayName": "Five Hour Limit Remaining", "window": "5h",
              "resetTime": "2026-08-11T13:04:36Z", "remainingFraction": 0.42 }
          ],
          "displayName": "Gemini Models",
          "description": "Models within this group: Gemini Flash, Gemini Pro"
        },
        {
          "buckets": [
            { "bucketId": "3p-weekly", "displayName": "Weekly Limit Remaining", "window": "weekly",
              "resetTime": "2026-08-18T08:04:36Z", "remainingFraction": 0.9 },
            { "bucketId": "3p-5h", "displayName": "Five Hour Limit Remaining", "window": "5h",
              "resetTime": "2026-08-11T13:04:36Z", "remainingFraction": 1 }
          ],
          "displayName": "Claude and GPT models",
          "description": "Models within this group: Claude Opus, Claude Sonnet, GPT-OSS"
        }
      ],
      "description": "Within each group, models share a weekly limit and a 5-hour limit."
    }
    """;

    [Fact]
    public void ParseSummary_FlattensGroupsIntoBuckets_WithComposedDisplayNames()
    {
        using var doc = JsonDocument.Parse(RealSummaryResponse);

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        Assert.Equal(4, buckets.Count);
        Assert.Equal(
            ["gemini-weekly", "gemini-5h", "3p-weekly", "3p-5h"],
            buckets.Select(b => b.ModelId));
        Assert.Equal("Gemini Models · Weekly Limit Remaining", buckets[0].DisplayName);
        Assert.Equal("Claude and GPT models · Five Hour Limit Remaining", buckets[3].DisplayName);
        Assert.Equal(["weekly", "5h", "weekly", "5h"], buckets.Select(b => b.TokenType));
        Assert.Equal(0.42, buckets[1].RemainingFraction, 3);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 18, 8, 4, 36, TimeSpan.Zero),
            buckets[0].ResetTime);
    }

    /// <summary>
    /// 2026-10-13 부터 Argon 대상 계정의 응답에 붙는 Gemini 4 Argon 그룹(#204).
    /// 이 PC 에는 Antigravity 가 없어 원문을 직접 받지 못했다 — 그룹·버킷 이름, 버킷 ID, 창은 다른 클라이언트들이
    /// 실제 응답에서 확인한 형태(tokenhop·LimitCounter·TaskWraith·openusage)를 따랐다. 대상이 아닌 창은 disabled 로 온다.
    /// </summary>
    private const string ArgonSummaryResponse = """
    {
      "groups": [
        {
          "buckets": [
            { "bucketId": "gemini-weekly", "displayName": "Weekly Limit Remaining", "window": "weekly",
              "resetTime": "2026-10-23T00:00:00Z", "remainingFraction": 1 },
            { "bucketId": "gemini-5h", "displayName": "Five Hour Limit Remaining", "window": "5h",
              "resetTime": "2026-10-16T09:00:00Z", "remainingFraction": 1 }
          ],
          "displayName": "Gemini Models"
        },
        {
          "buckets": [
            { "bucketId": "3p-weekly", "displayName": "Weekly Limit Remaining", "window": "weekly",
              "resetTime": "2026-10-23T00:00:00Z", "remainingFraction": 1 }
          ],
          "displayName": "Claude and GPT models"
        },
        {
          "buckets": [
            { "bucketId": "gemini-argon-wtu-weekly", "displayName": "Gemini 4 Argon Weekly Limit", "window": "weekly",
              "resetTime": "2026-10-22T23:00:00Z", "remainingFraction": 0.71 },
            { "bucketId": "gemini-argon-wtu-5h", "displayName": "Gemini 4 Argon Five Hour Limit", "window": "5h",
              "disabled": true, "remainingFraction": 1 }
          ],
          "displayName": "Gemini 4 Argon"
        }
      ]
    }
    """;

    [Fact]
    public void ParseSummary_KeepsArgonAsItsOwnGroup_WithoutRepeatingTheGroupName()
    {
        using var doc = JsonDocument.Parse(ArgonSummaryResponse);

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        var argon = Assert.Single(buckets, b => b.ModelId == "gemini-argon-wtu-weekly");
        Assert.Equal("Gemini 4 Argon", argon.GroupName);
        // 버킷 이름이 그룹 이름을 이미 품고 있어 "Gemini 4 Argon · Gemini 4 Argon Weekly Limit" 이 되면 안 된다.
        Assert.Equal("Gemini 4 Argon Weekly Limit", argon.DisplayName);
        Assert.Equal("weekly", argon.TokenType);
        Assert.Equal(0.71, argon.RemainingFraction, 3);
        // 이 계정이 쓰지 않는 Argon 5시간 창(disabled)은 행을 만들지 않는다.
        Assert.DoesNotContain(buckets, b => b.ModelId == "gemini-argon-wtu-5h");
        // 공유 풀도 그룹 이름을 함께 들고 있다.
        Assert.Equal("Gemini Models", buckets.Single(b => b.ModelId == "gemini-weekly").GroupName);
    }

    [Fact]
    public async Task CallV1Internal_AsksWithTheDesktopAppIdentity()
    {
        var handler = new StubHandler(_ => Json("""{"groups":[]}"""));
        using var monitor = new AntigravityUsageMonitor(handler);

        using var doc = await monitor.CallV1InternalAsync("retrieveUserQuotaSummary", "token", CancellationToken.None);

        Assert.NotNull(doc);
        // Argon 은 데스크톱 앱(hub) 계열 식별자에만 온다. 기본 헤더가 덧붙어 값이 두 개가 되어도 안 된다.
        Assert.Equal([AntigravityUsageMonitor.ClientUserAgent], handler.UserAgents);
        Assert.StartsWith("antigravity/hub/", AntigravityUsageMonitor.ClientUserAgent);
        Assert.Matches(@" windows/(amd64|arm64)$", AntigravityUsageMonitor.ClientUserAgent);
    }

    [Fact]
    public async Task CallV1Internal_RetriesWithTheLegacyIdentity_WhenTheServerRejectsTheNewOne()
    {
        var handler = new StubHandler(request =>
            StubHandler.UserAgentOf(request) == AntigravityUsageMonitor.ClientUserAgent
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Json("""{"groups":[]}"""));
        using var monitor = new AntigravityUsageMonitor(handler);

        using var doc = await monitor.CallV1InternalAsync("retrieveUserQuotaSummary", "token", CancellationToken.None);

        // 식별자 규칙이 바뀌어도 공유 풀 게이지는 예전 식별자로 계속 받는다.
        Assert.NotNull(doc);
        Assert.Equal(
            [AntigravityUsageMonitor.ClientUserAgent, AntigravityUsageMonitor.LegacyClientUserAgent],
            handler.UserAgents);
    }

    [Fact]
    public async Task CallV1Internal_DoesNotRetry_WhenTheTokenIsRejected()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var monitor = new AntigravityUsageMonitor(handler);

        using var doc = await monitor.CallV1InternalAsync("retrieveUserQuotaSummary", "token", CancellationToken.None);

        // 401 은 식별자가 아니라 토큰 문제다 — 다른 식별자로 다시 물어도 소용없다.
        Assert.Null(doc);
        Assert.Single(handler.UserAgents);
    }

    [Fact]
    public async Task CallV1Internal_ReturnsNull_WhenBothIdentitiesAreRejected()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var monitor = new AntigravityUsageMonitor(handler);

        using var doc = await monitor.CallV1InternalAsync("retrieveUserQuotaSummary", "token", CancellationToken.None);

        Assert.Null(doc);
        Assert.Equal(2, handler.UserAgents.Count);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        /// <summary>요청마다 실제로 붙은 User-Agent 값. 여러 개가 붙었으면 " | " 로 이어 드러낸다.</summary>
        public List<string> UserAgents { get; } = [];

        public static string UserAgentOf(HttpRequestMessage request) =>
            request.Headers.TryGetValues("User-Agent", out var values) ? string.Join(" | ", values) : "";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            UserAgents.Add(UserAgentOf(request));
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public void ParseSummary_UnwrapsResponseEnvelope_WhenServedByLocalLanguageServer()
    {
        // 로컬 language server 를 거치면 같은 본문이 "response" 아래로 한 겹 들어간다.
        using var doc = JsonDocument.Parse($$"""{"response": {{RealSummaryResponse}} }""");

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        Assert.Equal(4, buckets.Count);
        Assert.Equal("Gemini Models · Weekly Limit Remaining", buckets[0].DisplayName);
    }

    [Theory]
    [InlineData("""{"groups": []}""")]
    [InlineData("""{"groups": [{"displayName": "Gemini Models"}]}""")]   // buckets 누락
    [InlineData("""{"description": "no groups at all"}""")]
    [InlineData("""{"groups": "not-an-array"}""")]
    public void ParseSummary_ReturnsEmpty_WhenShapeIsUnexpected(string json)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Empty(AntigravityUsageMonitor.ParseSummary(doc));
    }

    [Fact]
    public void ParseSummary_ClampsFractionAndToleratesMissingFields()
    {
        using var doc = JsonDocument.Parse("""
        {
          "groups": [{
            "displayName": "Gemini Models",
            "buckets": [
              { "bucketId": "over",    "remainingFraction": 1.4 },
              { "bucketId": "under",   "remainingFraction": -0.2 },
              { "bucketId": "badreset", "remainingFraction": 0.5, "resetTime": "not-a-date" }
            ]
          }]
        }
        """);

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        Assert.Equal(1.0, buckets[0].RemainingFraction);
        Assert.Equal(0.0, buckets[1].RemainingFraction);
        Assert.Null(buckets[0].ResetTime);
        Assert.Null(buckets[2].ResetTime);                 // 파싱 실패한 시각은 지어내지 않는다
        Assert.Equal("Gemini Models", buckets[0].DisplayName);  // 버킷 이름이 없으면 그룹 이름만
    }

    [Fact]
    public void ParseSummary_SkipsBucketsWithoutFraction_InsteadOfInventingUsage()
    {
        // 잔여량은 remainingFraction 과 remainingAmount 중 하나만 온다(proto 의 oneof remaining).
        // 수량형은 한도를 모르니 사용률로 바꿀 수 없다 — 예전처럼 "다 남았다(1.0)" 로 두면
        // 다 쓴 버킷도 0% 로 그려진다.
        using var doc = JsonDocument.Parse("""
        {
          "groups": [{
            "displayName": "Gemini Models",
            "buckets": [
              { "bucketId": "gemini-5h", "window": "5h", "resetTime": "2026-08-11T13:04:36Z", "remainingFraction": 0 },
              { "bucketId": "gemini-requests", "window": "daily", "resetTime": "2026-08-12T00:00:00Z", "remainingAmount": "0" },
              { "bucketId": "gemini-unset", "window": "weekly", "resetTime": "2026-08-18T08:04:36Z" },
              { "bucketId": "gemini-text", "window": "weekly", "remainingFraction": "0.5" }
            ]
          }]
        }
        """);

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        // 0 은 "다 썼다" 는 값이므로 남는다. 숫자가 아닌 비율도 읽지 않는다.
        var only = Assert.Single(buckets);
        Assert.Equal("gemini-5h", only.ModelId);
        Assert.Equal(0.0, only.RemainingFraction);
    }

    [Fact]
    public void ParseSummary_SkipsDisabledBuckets_InsteadOfDrawingThemAsUnused()
    {
        // disabled 는 이 tier 가 계량하지 않는 창이다. remainingFraction 1 을 달고 와도 "0% 사용" 으로 그리면 안 된다 —
        // 주간 한도를 다 써 막힌 실제 사례(stablyai/orca#22511)에서 5시간 창이 이렇게 왔다.
        using var doc = JsonDocument.Parse("""
        {
          "groups": [{
            "displayName": "Gemini Models",
            "buckets": [
              { "bucketId": "gemini-weekly", "window": "weekly", "resetTime": "2026-08-13T08:04:36Z", "remainingFraction": 0 },
              { "bucketId": "gemini-5h", "window": "5h", "resetTime": "2026-08-12T13:04:36Z", "remainingFraction": 1, "disabled": true },
              { "bucketId": "gemini-flag", "window": "5h", "resetTime": "2026-08-12T13:04:36Z", "remainingFraction": 0.5, "disabled": false }
            ]
          }]
        }
        """);

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        Assert.Equal(["gemini-weekly", "gemini-flag"], buckets.Select(b => b.ModelId));
        Assert.Equal(0.0, buckets[0].RemainingFraction);   // 소진된 창은 그대로 남는다
    }

    [Fact]
    public void ParseSummary_ReadsRootBuckets_WhenGroupsAreAbsent()
    {
        // 응답 최상위에도 그룹 밖 buckets[] 자리가 있다(proto). 그룹이 없을 때의 대체로만 읽는다.
        using var doc = JsonDocument.Parse("""
        { "buckets": [ { "bucketId": "3p-5h", "displayName": "Five Hour Limit", "window": "5h",
                         "resetTime": "2026-08-11T13:04:36Z", "remainingFraction": 0.25 } ] }
        """);

        var bucket = Assert.Single(AntigravityUsageMonitor.ParseSummary(doc));
        Assert.Equal("3p-5h", bucket.ModelId);
        Assert.Equal(0.25, bucket.RemainingFraction, 3);
        Assert.Equal("Five Hour Limit", bucket.DisplayName);   // 그룹 이름이 없으면 버킷 이름만
    }

    [Fact]
    public void ParseSummary_IgnoresRootBuckets_WhenGroupsAlreadyGaveRows()
    {
        // 같은 버킷을 그룹 없이 다시 실은 것일 수 있다 — 그룹에서 행을 얻었으면 두 번 그리지 않는다.
        using var doc = JsonDocument.Parse("""
        {
          "groups": [{
            "displayName": "Gemini Models",
            "buckets": [ { "bucketId": "gemini-5h", "window": "5h", "resetTime": "2026-08-11T13:04:36Z", "remainingFraction": 0.42 } ]
          }],
          "buckets": [
            { "bucketId": "gemini-5h", "window": "5h", "resetTime": "2026-08-11T13:04:36Z", "remainingFraction": 0.99 },
            { "bucketId": "gemini-weekly", "window": "weekly", "resetTime": "2026-08-18T08:04:36Z", "remainingFraction": 0.7 }
          ]
        }
        """);

        var bucket = Assert.Single(AntigravityUsageMonitor.ParseSummary(doc));
        Assert.Equal("gemini-5h", bucket.ModelId);
        Assert.Equal(0.42, bucket.RemainingFraction, 3);
    }

    // ---------- loadCodeAssist: tier · Google AI 크레딧 ----------

    [Fact]
    public void ParseCodeAssistTier_ReadsTierNamesAndGoogleOneAiCredits()
    {
        // creditAmount 는 int64 라 JSON 에서 문자열로 온다(gemini-cli types.ts 의 Credits).
        using var doc = JsonDocument.Parse("""
        {
          "currentTier": { "id": "standard-tier", "name": "Gemini Code Assist" },
          "paidTier": {
            "id": "g1-pro-tier",
            "name": "Gemini Code Assist in Google One AI Pro",
            "availableCredits": [
              { "creditType": "GOOGLE_ONE_AI", "creditAmount": "1000", "minimumCreditAmountForUsage": "50" }
            ]
          }
        }
        """);

        var tier = AntigravityUsageMonitor.ParseCodeAssistTier(doc);

        Assert.Equal("Gemini Code Assist", tier.TierName);
        Assert.Equal("Gemini Code Assist in Google One AI Pro", tier.PaidTierName);
        Assert.Equal(1000, tier.AiCredits);
    }

    [Theory]
    // 크레딧 대상이 아닌 계정 — 줄을 그리지 않는다.
    [InlineData("""{ "currentTier": { "name": "Gemini Code Assist" } }""", null)]
    [InlineData("""{ "paidTier": { "name": "Pro" } }""", null)]
    [InlineData("""{ "paidTier": { "availableCredits": [] } }""", null)]
    [InlineData("""{ "paidTier": { "availableCredits": [ { "creditType": "CREDIT_TYPE_UNSPECIFIED", "creditAmount": "5" } ] } }""", null)]
    // 대상이지만 다 쓴 계정 — 0 도 값이다(gemini-cli getG1CreditBalance 와 같은 규칙).
    [InlineData("""{ "paidTier": { "availableCredits": [ { "creditType": "GOOGLE_ONE_AI", "creditAmount": "0" } ] } }""", 0L)]
    [InlineData("""{ "paidTier": { "availableCredits": [ { "creditType": "GOOGLE_ONE_AI" } ] } }""", 0L)]
    // 여러 항목은 더하고, 다른 종류는 빼며, 숫자로 와도 읽고, 못 읽는 값은 0 으로 센다.
    [InlineData("""
    { "paidTier": { "availableCredits": [
        { "creditType": "GOOGLE_ONE_AI", "creditAmount": "700" },
        { "creditType": "CREDIT_TYPE_UNSPECIFIED", "creditAmount": "9999" },
        { "creditType": "GOOGLE_ONE_AI", "creditAmount": 300 },
        { "creditType": "GOOGLE_ONE_AI", "creditAmount": "lots" }
    ] } }
    """, 1000L)]
    public void ParseCodeAssistTier_ReadsCreditsOnlyFromGoogleOneAiEntries(string json, long? expected)
    {
        using var doc = JsonDocument.Parse(json);

        Assert.Equal(expected, AntigravityUsageMonitor.ParseCodeAssistTier(doc).AiCredits);
    }

    [Fact]
    public void ParseCodeAssistTier_IgnoresCreditsOnCurrentTier()
    {
        // gemini-cli 와 다른 클라이언트 모두 paidTier 쪽만 읽는다 — 두 곳을 더하면 같은 잔액을 두 번 셀 수 있다.
        using var doc = JsonDocument.Parse("""
        { "currentTier": { "name": "Free", "availableCredits": [ { "creditType": "GOOGLE_ONE_AI", "creditAmount": "10" } ] } }
        """);

        Assert.Null(AntigravityUsageMonitor.ParseCodeAssistTier(doc).AiCredits);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""{ "currentTier": "standard", "paidTier": 3 }""")]
    [InlineData("""{ "currentTier": { "name": 7 }, "paidTier": { "availableCredits": "none" } }""")]
    public void ParseCodeAssistTier_ReturnsEmpty_WhenShapeIsUnexpected(string json)
    {
        // 배지·크레딧은 부가 정보라, 모양이 달라도 예외 대신 빈 값으로 넘어가 게이지를 막지 않는다.
        using var doc = JsonDocument.Parse(json);

        var tier = AntigravityUsageMonitor.ParseCodeAssistTier(doc);

        Assert.Null(tier.TierName);
        Assert.Null(tier.PaidTierName);
        Assert.Null(tier.AiCredits);
    }

    [Theory]
    [InlineData("Gemini Models", "Weekly Limit", "Gemini Models · Weekly Limit")]
    [InlineData("", "Weekly Limit", "Weekly Limit")]
    [InlineData("Gemini Models", "", "Gemini Models")]
    [InlineData("", "", "")]
    [InlineData("  Gemini Models  ", "  Weekly Limit  ", "Gemini Models · Weekly Limit")]
    // 모델 한정 그룹은 버킷 이름에 그룹 이름이 이미 들어 있다 — 겹쳐 붙이지 않는다(#204).
    [InlineData("Gemini 4 Argon", "Gemini 4 Argon Weekly Limit", "Gemini 4 Argon Weekly Limit")]
    public void ComposeDisplayName_JoinsAvailableParts(string group, string bucket, string expected)
    {
        Assert.Equal(expected, AntigravityUsageMonitor.ComposeDisplayName(group, bucket));
    }

    [Fact]
    public void ParseCredentialBlob_ReadsTokensAndExpiry()
    {
        var cred = AntigravityUsageMonitor.ParseCredentialBlob("""
        {
          "token": {
            "access_token": "ya29.sample",
            "token_type": "Bearer",
            "refresh_token": "1//sample",
            "expiry": "2026-08-11T17:36:25Z"
          },
          "id_token": "should-not-be-read",
          "auth_method": "consumer"
        }
        """);

        Assert.NotNull(cred);
        Assert.Equal("ya29.sample", cred!.AccessToken);
        Assert.Equal("1//sample", cred.RefreshToken);
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 17, 36, 25, TimeSpan.Zero), cred.ExpiresAt);
    }

    [Fact]
    public void ParseCredentialBlob_ReturnsNull_WhenTokenObjectMissing()
    {
        Assert.Null(AntigravityUsageMonitor.ParseCredentialBlob("""{"auth_method":"consumer"}"""));
    }

    [Fact]
    public void ParseCredentialBlob_KeepsRefreshToken_WhenExpiryUnparsable()
    {
        // 만료 시각을 못 읽으면 access_token 을 신뢰하지 않고 갱신 경로로 가야 한다.
        var cred = AntigravityUsageMonitor.ParseCredentialBlob("""
        {"token": {"access_token": "ya29.sample", "refresh_token": "1//sample", "expiry": "0001-01-01"}}
        """);

        Assert.NotNull(cred);
        Assert.Equal("1//sample", cred!.RefreshToken);
    }
}
