using System.Text.RegularExpressions;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 여러 PC 가 같은 ntfy 토픽으로 같은 알림을 각자 보내던 문제(#175)의 회귀 테스트.
// 시간은 고정 기준 시각(Now)에서 상대값으로 만들어 실행 시각에 좌우되지 않게 한다.
public class NtfyEventKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = Now.AddHours(3);

    // ntfy 서버(server/server.go)의 sequenceIDRegex. 어기면 HTTP 400 으로 알림이 거부된다.
    private static readonly Regex NtfySequenceId = new("^[-_A-Za-z0-9]{1,64}$");

    private static string Message(string sequenceId, string text = "x", string ev = "message") =>
        $"{{\"id\":\"abc\",\"time\":1790770431,\"event\":\"{ev}\",\"topic\":\"t\",\"sequence_id\":\"{sequenceId}\",\"message\":\"{text}\"}}";

    // ---- 형식 ----

    [Fact]
    public void SequenceId_AlwaysSatisfiesTheNtfyFormat_EvenForOddOrHugeNames()
    {
        var hugeAgent = new string('a', 300);
        var names = new[] { "Claude", "Gemini CLI", "코덱스", "a/b c", "A_B", "", "  ", hugeAgent, new string('가', 200) };

        foreach (var name in names)
        {
            var keys = new[]
            {
                NtfyEventKey.ForUsage(name, name, 90, Reset, Now),
                NtfyEventKey.ForUsage(name, name, 90, null, Now),
                NtfyEventKey.ForInstant(name, name, Now),
                NtfyEventKey.ForEarlyExhaustion(name, Now.AddHours(1), Now),
                NtfyEventKey.ForExact(name, name + "/한글 키"),
            };

            foreach (var key in keys)
                Assert.True(NtfySequenceId.IsMatch(key.SequenceId), $"'{name}' -> '{key.SequenceId}'");
        }
    }

    // 접두사가 한도를 넘어 잘려도 서로 다른 키가 한 키로 합쳐지면 안 된다(엉뚱한 알림이 삼켜진다).
    [Fact]
    public void LongPrefixes_StayDistinct_WhenOnlyTheTailDiffers()
    {
        var a = NtfyEventKey.ForUsage(new string('a', 100) + "-one", "short", 90, Reset, Now);
        var b = NtfyEventKey.ForUsage(new string('a', 100) + "-two", "short", 90, Reset, Now);

        Assert.NotEqual(a.SequenceId, b.SequenceId);
        Assert.False(a.Matches(b.SequenceId));
    }

    [Theory]
    [InlineData("Claude", "claude")]
    [InlineData("Gemini CLI", "gemini-cli")]
    [InlineData("  Open  Code  ", "open-code")]
    [InlineData("코덱스", "x")]
    [InlineData("A_B", "a-b")]
    public void Slug_KeepsAsciiLettersDigitsAndHyphensOnly(string input, string expected) =>
        Assert.Equal(expected, NtfyEventKey.Slug(input));

    // ---- 사용량 알림 ----

    // 이번 버그의 핵심: PC 가 달라도, 조회 시각이 달라도 같은 창의 같은 임계값은 같은 사건이다.
    [Fact]
    public void Usage_SameWindowFromAnotherPc_IsTheSameEvent()
    {
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);

        // PC-B 는 2분 늦게 감지했고 서버 리셋 시각을 1초 다르게 읽었다.
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, Reset.AddSeconds(1), Now.AddMinutes(2));

        Assert.True(pcB.Matches(pcA.SequenceId));
        Assert.True(pcA.Matches(pcB.SequenceId));
    }

    // 새 창에서 다시 90% 에 도달한 것은 진짜 새 알림이다 — 이것을 삼키면 알림을 놓친다.
    [Fact]
    public void Usage_NextWindow_IsANewEvent()
    {
        var thisWindow = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);
        var nextWindow = NtfyEventKey.ForUsage("Codex", "short", 90, Reset.AddHours(5), Now.AddHours(5));

        Assert.False(nextWindow.Matches(thisWindow.SequenceId));
    }

    [Fact]
    public void Usage_DifferentThresholdAgentOrWindow_AreDifferentEvents()
    {
        var baseKey = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);

        Assert.False(NtfyEventKey.ForUsage("Codex", "short", 75, Reset, Now).Matches(baseKey.SequenceId));
        Assert.False(NtfyEventKey.ForUsage("Claude", "short", 90, Reset, Now).Matches(baseKey.SequenceId));
        Assert.False(NtfyEventKey.ForUsage("Codex", "extra", 90, Reset, Now).Matches(baseKey.SequenceId));
    }

    // 리셋 시각을 모르면(추정치·없음) 감지 시각으로 대신한다. 몇 분 늦은 감지는 같은 사건, 오래 지나면 새 사건.
    [Theory]
    [InlineData(2, true)]
    [InlineData(29, true)]
    [InlineData(31, false)]
    [InlineData(300, false)]
    public void Usage_WithoutResetTime_FallsBackToDetectionTime(int minutesLater, bool expectedSame)
    {
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, null, Now);
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, null, Now.AddMinutes(minutesLater));

        Assert.Equal(expectedSame, pcB.Matches(pcA.SequenceId));
    }

    // ---- 초기화·레이트 리밋 ----

    [Fact]
    public void Instant_SameKindAndAgentNearInTime_IsTheSameEvent_ButNotAcrossKindsOrAgents()
    {
        var reset = NtfyEventKey.ForInstant("reset", "Codex", Now);

        Assert.True(NtfyEventKey.ForInstant("reset", "Codex", Now.AddMinutes(3)).Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("reset", "Claude", Now).Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("ratelimit", "Codex", Now).Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("reset", "Codex", Now.AddHours(6)).Matches(reset.SequenceId));
    }

    // ---- 조기 소진 ----

    [Fact]
    public void EarlyExhaustion_SimilarEstimates_AreOneEvent_ButAMuchEarlierEstimateIsNew()
    {
        var depletion = Now.AddHours(2);
        var first = NtfyEventKey.ForEarlyExhaustion("Claude", depletion, Now);

        Assert.True(NtfyEventKey.ForEarlyExhaustion("Claude", depletion.AddMinutes(6), Now.AddMinutes(1)).Matches(first.SequenceId));
        Assert.False(NtfyEventKey.ForEarlyExhaustion("Claude", depletion.AddMinutes(-40), Now.AddMinutes(30)).Matches(first.SequenceId));
    }

    // ---- 날씨(정확 일치) ----

    [Fact]
    public void Exact_MatchesOnlyTheIdenticalKey()
    {
        var daily = NtfyEventKey.ForExact("weather", "daily:20260930:37.57:126.98");

        Assert.True(NtfyEventKey.ForExact("weather", "daily:20260930:37.57:126.98").Matches(daily.SequenceId));
        Assert.False(NtfyEventKey.ForExact("weather", "daily:20261001:37.57:126.98").Matches(daily.SequenceId));
        Assert.False(NtfyEventKey.ForExact("weather", "daily:20260930:35.18:129.07").Matches(daily.SequenceId));
    }

    [Fact]
    public void Exact_NeverMatchesAUsageSequenceId()
    {
        var usage = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);
        var weather = NtfyEventKey.ForExact("weather", "daily:20260930:37.57:126.98");

        Assert.False(weather.Matches(usage.SequenceId));
        Assert.False(usage.Matches(weather.SequenceId));
    }

    // ---- 캐시 응답 훑기 ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("usage-codex-short-90")]
    [InlineData("usage-codex-short-90_")]
    [InlineData("usage-codex-short-90_abc")]
    [InlineData("usage-codex-short-90_-5")]
    [InlineData("usage-codex-short-9_1790000000")]
    public void Matches_RejectsMalformedSequenceIds(string? sequenceId)
    {
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, DateTimeOffset.FromUnixTimeSeconds(1790000000), Now);

        Assert.False(key.Matches(sequenceId));
    }

    // 다른 PC 가 보낸 메시지는 본문에 PC 이름·상대 시간이 달라도 sequence_id 만 같으면 같은 사건이다.
    [Fact]
    public void IsAlreadySent_IgnoresMessageText_AndMatchesOnSequenceIdOnly()
    {
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now.AddMinutes(2));
        var fromOtherPc = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);

        var ndjson = Message(fromOtherPc.SequenceId, "[Codex] 사용량이 90%에 도달했습니다 ·1시간 23분 후 초기화\\n— OTHER-PC");

        Assert.True(key.IsAlreadySent(ndjson));
    }

    [Fact]
    public void IsAlreadySent_FindsTheMatchAmongOtherAndBrokenLines()
    {
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);
        var other = NtfyEventKey.ForUsage("Codex", "short", 75, Reset, Now);

        var ndjson = string.Join('\n',
            "not json at all",
            "[1,2,3]",
            "{\"event\":\"open\"}",
            Message(other.SequenceId),
            "{\"id\":\"old-app-message\",\"event\":\"message\",\"message\":\"no sequence id\"}",
            Message(key.SequenceId));

        Assert.True(key.IsAlreadySent(ndjson));
    }

    [Fact]
    public void IsAlreadySent_IsFalse_WhenOnlyOtherEventsOrNoSequenceIdsExist()
    {
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);
        var otherWindow = NtfyEventKey.ForUsage("Codex", "short", 90, Reset.AddHours(5), Now.AddHours(5));

        Assert.False(key.IsAlreadySent(null));
        Assert.False(key.IsAlreadySent(""));
        Assert.False(key.IsAlreadySent("{\"id\":\"old\",\"event\":\"message\",\"message\":\"no sequence id\"}"));
        Assert.False(key.IsAlreadySent(Message(otherWindow.SequenceId)));
    }

    // 삭제·읽음 처리된 시퀀스는 "이미 보냈다" 의 근거가 아니다.
    [Theory]
    [InlineData("message_delete")]
    [InlineData("message_clear")]
    public void IsAlreadySent_IgnoresDeleteAndClearEvents(string ev)
    {
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);

        Assert.False(key.IsAlreadySent(Message(key.SequenceId, ev: ev)));
    }
}
