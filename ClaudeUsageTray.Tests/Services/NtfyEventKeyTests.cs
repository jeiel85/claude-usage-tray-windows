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

    // 같은 계정으로 로그인한 여러 PC 를 흉내 낼 때 쓴다. 계정을 모르는 PC 끼리는 일치하지 않는 것이 맞는 동작이다.
    private const string Acct = "acct-shared";

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
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: Acct);

        // PC-B 는 2분 늦게 감지했고 서버 리셋 시각을 1초 다르게 읽었다.
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, Reset.AddSeconds(1), Now.AddMinutes(2), accountId: Acct);

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
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, null, Now, accountId: Acct);
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, null, Now.AddMinutes(minutesLater), accountId: Acct);

        Assert.Equal(expectedSame, pcB.Matches(pcA.SequenceId));
    }

    // ---- 계정 ----

    // Codex 리뷰봇 지적(#176): 회사·개인 계정처럼 서로 다른 계정의 PC 가 같은 토픽을 쓰면, 공급자·창·임계값이 같은 알림이
    // 같은 키가 되어 뒤에 감지한 쪽의 정당한 알림이 삼켜졌다. 고정 시각 창(Codex 주간 등)에서는 더 그럴듯하다.
    [Fact]
    public void Usage_DifferentAccountsWithTheSameWindow_AreDifferentEvents()
    {
        var work = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: "acct-work|user-1");
        var personal = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: "acct-personal|user-2");

        Assert.False(personal.Matches(work.SequenceId));
        Assert.False(work.Matches(personal.SequenceId));
    }

    // 반대로 같은 계정이면 어느 PC 에서든 같은 사건이어야 중복이 걸러진다.
    [Fact]
    public void Usage_SameAccountFromTwoPcs_IsStillTheSameEvent()
    {
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: "acct-work|user-1");
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, Reset.AddSeconds(1), Now.AddMinutes(2), accountId: " acct-work|user-1 ");

        Assert.True(pcB.Matches(pcA.SequenceId));
    }

    // 같은 워크스페이스(account_id)의 다른 사용자도 구분되어야 한다 — 식별자 원문 전체가 해시에 들어간다.
    [Fact]
    public void Usage_SameWorkspaceDifferentUsers_AreDifferentEvents()
    {
        var alice = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: "workspace-1|alice");
        var bob = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: "workspace-1|bob");

        Assert.False(bob.Matches(alice.SequenceId));
    }

    [Fact]
    public void Instant_And_EarlyExhaustion_AreScopedToTheAccountToo()
    {
        var reset = NtfyEventKey.ForInstant("reset", "Claude", Now, "org-1");
        var early = NtfyEventKey.ForEarlyExhaustion("Claude", Now.AddHours(2), Now, "org-1");

        Assert.True(NtfyEventKey.ForInstant("reset", "Claude", Now.AddMinutes(2), "org-1").Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("reset", "Claude", Now.AddMinutes(2), "org-2").Matches(reset.SequenceId));
        Assert.True(NtfyEventKey.ForEarlyExhaustion("Claude", Now.AddHours(2).AddMinutes(3), Now, "org-1").Matches(early.SequenceId));
        Assert.False(NtfyEventKey.ForEarlyExhaustion("Claude", Now.AddHours(2), Now, "org-2").Matches(early.SequenceId));
    }

    // 계정을 못 읽은 PC(null)는 알려진 PC 와 일치하지 않는다 — 중복이 걸러지지 않을 뿐 알림은 잃지 않는다.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Usage_UnknownAccount_NeverMatchesAKnownAccount_AndBlankMeansUnknown(string? blank)
    {
        var known = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: "acct-1");
        var unknown = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: blank);

        Assert.False(unknown.Matches(known.SequenceId));
        Assert.False(known.Matches(unknown.SequenceId));
        Assert.Equal(NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now).SequenceId, unknown.SequenceId);
    }

    // 토픽 캐시는 공개 인프라에 남는다 — 계정 식별자 원문은 절대 sequence_id 에 실리면 안 된다.
    [Fact]
    public void SequenceId_NeverContainsTheRawAccountIdentifier()
    {
        const string raw = "3f2b1c9e-7a44-4d0e-9d55-0a1b2c3d4e5f";

        var key = NtfyEventKey.ForUsage("Claude", "short", 90, Reset, Now, accountId: raw);

        Assert.DoesNotContain(raw, key.SequenceId);
        Assert.DoesNotContain("3f2b1c9e", key.SequenceId);
        Assert.Matches(NtfySequenceId, key.SequenceId);
    }

    // 접두사가 한도를 넘어 잘려도 계정 구간이 살아 있어야 한다(전체 접두사의 해시로 보완하므로 계정이 다르면 결과도 다르다).
    [Fact]
    public void LongNames_StillSeparateAccounts()
    {
        var name = new string('a', 100);
        var a = NtfyEventKey.ForUsage(name, "short", 90, Reset, Now, accountId: "acct-1");
        var b = NtfyEventKey.ForUsage(name, "short", 90, Reset, Now, accountId: "acct-2");

        Assert.NotEqual(a.SequenceId, b.SequenceId);
        Assert.False(b.Matches(a.SequenceId));
        Assert.Matches(NtfySequenceId, a.SequenceId);
    }

    // Codex 리뷰봇 두 번째 지적(#176): 계정을 모르는 PC 끼리(예: API 키 모드라 auth.json 에 tokens 가 없는 서로 다른 계정)
    // "모름" 이라는 이유로 같은 키가 되면 뒤에 감지한 쪽의 정당한 알림이 삼켜진다. 모르면 기기 구분자로 갈라야 한다.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnknownAccounts_OnDifferentDevices_NeverMatch(string? unknown)
    {
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, unknown, deviceId: "pc-a|alice");
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, unknown, deviceId: "pc-b|bob");

        Assert.False(pcB.Matches(pcA.SequenceId));
        Assert.False(pcA.Matches(pcB.SequenceId));
    }

    [Fact]
    public void UnknownAccounts_OnDifferentDevices_NeverMatch_ForEveryAlertKind()
    {
        var resetA = NtfyEventKey.ForInstant("reset", "Codex", Now, null, "pc-a|alice");
        var resetB = NtfyEventKey.ForInstant("reset", "Codex", Now, null, "pc-b|bob");
        var earlyA = NtfyEventKey.ForEarlyExhaustion("Claude", Now.AddHours(2), Now, null, "pc-a|alice");
        var earlyB = NtfyEventKey.ForEarlyExhaustion("Claude", Now.AddHours(2), Now, null, "pc-b|bob");

        Assert.False(resetB.Matches(resetA.SequenceId));
        Assert.False(earlyB.Matches(earlyA.SequenceId));
    }

    // 같은 PC 의 반복(앱 재시작·중복 새로고침)은 계정을 몰라도 여전히 걸러진다.
    [Fact]
    public void UnknownAccount_OnTheSameDevice_StillDeduplicatesItself()
    {
        var first = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, null, deviceId: "pc-a|alice");
        var again = NtfyEventKey.ForUsage("Codex", "short", 90, Reset.AddSeconds(1), Now.AddMinutes(2), null, deviceId: "pc-a|alice");

        Assert.True(again.Matches(first.SequenceId));
    }

    // 계정을 알면 기기와 무관하다 — 같은 계정의 두 PC 는 여전히 같은 사건이어야 중복이 걸러진다.
    [Fact]
    public void KnownAccount_IgnoresTheDevice()
    {
        var pcA = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, Acct, deviceId: "pc-a|alice");
        var pcB = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, Acct, deviceId: "pc-b|bob");

        Assert.Equal(pcA.SequenceId, pcB.SequenceId);
        Assert.True(pcB.Matches(pcA.SequenceId));
    }

    // 계정 조각("-a…")과 기기 조각("-d…")은 접두사가 달라 서로 일치할 수 없다.
    [Fact]
    public void KnownAndUnknownAccounts_NeverMatch_EvenWhenTheHashesCouldCollide()
    {
        var known = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, "pc-a|alice", deviceId: "x");
        var unknown = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, null, deviceId: "pc-a|alice");

        Assert.False(unknown.Matches(known.SequenceId));
        Assert.False(known.Matches(unknown.SequenceId));
    }

    // 기기 식별자 원문(컴퓨터 이름·사용자 이름)도 공개 토픽 캐시에 남기지 않는다.
    [Fact]
    public void SequenceId_NeverContainsTheRawDeviceIdentifier()
    {
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, null, deviceId: "MY-DESKTOP|alice");

        Assert.DoesNotContain("MY-DESKTOP", key.SequenceId, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", key.SequenceId, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(NtfySequenceId, key.SequenceId);
    }

    // 기본값(앱이 실제로 쓰는 경로)은 이 PC 의 식별자를 쓰므로 같은 프로세스에서는 항상 같은 키가 나온다.
    [Fact]
    public void DefaultDevice_IsStableWithinAProcess()
    {
        var a = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);
        var b = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now);

        Assert.Equal(a.SequenceId, b.SequenceId);
    }

    // ---- 초기화·레이트 리밋 ----

    [Fact]
    public void Instant_SameKindAndAgentNearInTime_IsTheSameEvent_ButNotAcrossKindsOrAgents()
    {
        var reset = NtfyEventKey.ForInstant("reset", "Codex", Now, Acct);

        Assert.True(NtfyEventKey.ForInstant("reset", "Codex", Now.AddMinutes(3), Acct).Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("reset", "Claude", Now, Acct).Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("ratelimit", "Codex", Now, Acct).Matches(reset.SequenceId));
        Assert.False(NtfyEventKey.ForInstant("reset", "Codex", Now.AddHours(6), Acct).Matches(reset.SequenceId));
    }

    // ---- 조기 소진 ----

    [Fact]
    public void EarlyExhaustion_SimilarEstimates_AreOneEvent_ButAMuchEarlierEstimateIsNew()
    {
        var depletion = Now.AddHours(2);
        var first = NtfyEventKey.ForEarlyExhaustion("Claude", depletion, Now, Acct);

        Assert.True(NtfyEventKey.ForEarlyExhaustion("Claude", depletion.AddMinutes(6), Now.AddMinutes(1), Acct).Matches(first.SequenceId));
        Assert.False(NtfyEventKey.ForEarlyExhaustion("Claude", depletion.AddMinutes(-40), Now.AddMinutes(30), Acct).Matches(first.SequenceId));
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
        var key = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now.AddMinutes(2), accountId: Acct);
        var fromOtherPc = NtfyEventKey.ForUsage("Codex", "short", 90, Reset, Now, accountId: Acct);

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
