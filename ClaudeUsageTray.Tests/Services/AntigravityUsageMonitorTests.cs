using System;
using System.Linq;
using System.Text.Json;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 응답·자격 증명 파싱만 검증한다. HTTP 자체는 비공식 API 라 테스트로 고정하지 않는다.
/// 픽스처는 실제 retrieveUserQuotaSummary 응답에서 가져왔다.
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
              { "bucketId": "missing" },
              { "bucketId": "badreset", "remainingFraction": 0.5, "resetTime": "not-a-date" }
            ]
          }]
        }
        """);

        var buckets = AntigravityUsageMonitor.ParseSummary(doc);

        Assert.Equal(1.0, buckets[0].RemainingFraction);
        Assert.Equal(0.0, buckets[1].RemainingFraction);
        Assert.Equal(1.0, buckets[2].RemainingFraction);   // 값이 없으면 "다 남았다" 로 둔다
        Assert.Null(buckets[2].ResetTime);
        Assert.Null(buckets[3].ResetTime);                 // 파싱 실패한 시각은 지어내지 않는다
        Assert.Equal("Gemini Models", buckets[2].DisplayName);  // 버킷 이름이 없으면 그룹 이름만
    }

    [Theory]
    [InlineData("Gemini Models", "Weekly Limit", "Gemini Models · Weekly Limit")]
    [InlineData("", "Weekly Limit", "Weekly Limit")]
    [InlineData("Gemini Models", "", "Gemini Models")]
    [InlineData("", "", "")]
    [InlineData("  Gemini Models  ", "  Weekly Limit  ", "Gemini Models · Weekly Limit")]
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
