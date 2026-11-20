using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Claude 계정 별명(#307): 계정마다 따로 붙고, 알림 제목 한 줄에 들어가게 정리된다.
/// PC 간 동기화(#313): 바꾼 시각을 함께 두고, 더 최근에 바꾼 쪽(지운 것 포함)을 모든 PC 가 따른다.
/// </summary>
public class AccountAliasTests
{
    private static readonly DateTimeOffset T1 = new(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = T1.AddMinutes(5);

    [Fact]
    public void CleanKeepsOneShortLine()
    {
        Assert.Equal("회사 계정", AccountAlias.Clean("  회사\n\t계정  "));
        Assert.Equal("", AccountAlias.Clean("   "));
        Assert.Equal("", AccountAlias.Clean(null));
        // 알림 제목 맨 앞에 들어가므로 20자에서 자른다.
        Assert.Equal(AccountAlias.MaxLength, AccountAlias.Clean(new string('가', 50)).Length);
    }

    [Fact]
    public void EachAccountKeepsItsOwnAlias()
    {
        // 같은 사람이라도 개인 조직과 팀 조직은 다른 계정이다(accountUuid 는 같고 orgUuid 가 다르다).
        var aliases = AccountAlias.With(null, "personal-org|acct", "개인", T1);
        aliases = AccountAlias.With(aliases, "team-org|acct", "회사", T1);

        Assert.Equal("개인", AccountAlias.Find(aliases, "personal-org|acct"));
        Assert.Equal("회사", AccountAlias.Find(aliases, "team-org|acct"));
        Assert.Equal("", AccountAlias.Find(aliases, "other-org|acct"));
        Assert.Equal("", AccountAlias.Find(aliases, null));
    }

    [Fact]
    public void ClearingLeavesATimedRecordForOtherPcs()
    {
        var aliases = AccountAlias.With(AccountAlias.With(null, "a", "개인", T1), "b", "회사", T1);

        var next = AccountAlias.With(aliases, "a", "  ", T2);

        // 지운 것도 시각과 함께 남는다 — 다른 PC 가 옛 별명을 계속 쓰지 않게 전해야 한다.
        Assert.Equal(new AccountAliasEntry("", T2), AccountAlias.Entry(next, "a"));
        Assert.Equal("", AccountAlias.Find(next, "a"));
        Assert.Equal("회사", AccountAlias.Find(next, "b"));
        // 원래 사전은 그대로다 — 백그라운드에서 읽는 쪽이 반쯤 바뀐 사전을 보지 않게 새 사전을 만든다.
        Assert.Equal("개인", AccountAlias.Find(aliases, "a"));
        // 없던 별명을 지우는 것은 남길 기록이 없다.
        Assert.Null(AccountAlias.Entry(AccountAlias.With(null, "c", "", T2), "c"));
    }

    [Fact]
    public void WithoutAnAccountKeyNothingIsAdded()
    {
        Assert.Empty(AccountAlias.With(null, null, "회사", T1));
    }

    [Fact]
    public void TheMoreRecentChangeWins()
    {
        var local = new AccountAliasEntry("개인", T1);

        Assert.True(AccountAlias.Wins(new AccountAliasEntry("회사", T2), local));
        Assert.False(AccountAlias.Wins(new AccountAliasEntry("회사", T1.AddMinutes(-1)), local));
        // 더 최근에 지운 것도 이긴다.
        Assert.True(AccountAlias.Wins(new AccountAliasEntry("", T2), local));
        // 이 PC 에 기록이 없으면 별명이 있을 때만 따른다(지운 기록만으로는 바꿀 것이 없다).
        Assert.True(AccountAlias.Wins(new AccountAliasEntry("회사", DateTimeOffset.MinValue), null));
        Assert.False(AccountAlias.Wins(new AccountAliasEntry("", T2), null));
        // 같은 기록은 이기지 않는다 — 서로 주고받아도 계속 바뀌지 않는다.
        Assert.False(AccountAlias.Wins(local, local));
    }

    [Fact]
    public void ATieIsSettledTheSameWayOnEveryPc()
    {
        // 둘 다 시각이 없던 v1.70.0 별명처럼 시각이 같으면 문자열 순서로 정한다 — 양쪽 PC 가 같은 쪽을 고른다.
        var a = new AccountAliasEntry("개인", DateTimeOffset.MinValue);
        var b = new AccountAliasEntry("회사", DateTimeOffset.MinValue);
        Assert.NotEqual(AccountAlias.Wins(a, b), AccountAlias.Wins(b, a));
    }

    [Fact]
    public void PickSyncedTakesOnlyThisPersonFromOtherPcs()
    {
        static UsageSyncSnapshot Snap(string device, string keyHash, string alias, DateTimeOffset at) => new()
        {
            DeviceId = device,
            AccountAlias = new UsageSyncAccountAlias { KeyHash = keyHash, Alias = alias, UpdatedAtUtc = at },
        };

        var picked = AccountAlias.PickSynced(
        [
            Snap("pc-a", "mine", "회사", T1),
            Snap("pc-b", "mine", "회사2", T2),
            // 같은 팀 조직의 다른 사람 — 스냅샷 묶음은 같아도 별명 키가 다르다.
            Snap("pc-c", "someone-else", "남의 별명", T2.AddHours(1)),
            // 이 PC 자신의 기록은 다시 따르지 않는다.
            Snap("me", "mine", "내 것", T2.AddHours(1)),
            new UsageSyncSnapshot { DeviceId = "old-pc" },
        ], "mine", "me");

        Assert.Equal(new AccountAliasEntry("회사2", T2), picked);
        Assert.Null(AccountAlias.PickSynced([new UsageSyncSnapshot { DeviceId = "old-pc" }], "mine", "me"));
    }

    [Fact]
    public void SettingsKeepTheV170ShapeAndAddTimes()
    {
        var entries = AccountAlias.With(AccountAlias.With(null, "a", "개인", T1), "b", "회사", T1);
        entries = AccountAlias.With(entries, "b", "", T2);
        entries = AccountAlias.Adopt(entries, "c", new AccountAliasEntry("옛 별명", DateTimeOffset.MinValue));

        var (aliases, times) = AccountAlias.ToSettings(entries);

        // 별명 사전은 v1.70.0 과 같은 모양(별명이 있는 계정만) — 되돌려 설치해도 별명이 남는다.
        Assert.Equal(new Dictionary<string, string> { ["a"] = "개인", ["c"] = "옛 별명" }, aliases);
        // 시각은 아는 것만(지운 기록 포함). 시각 없는 v1.70.0 별명은 적지 않는다.
        Assert.Equal(new Dictionary<string, DateTimeOffset> { ["a"] = T1, ["b"] = T2 }, times);

        var back = AccountAlias.FromSettings(aliases, times);
        Assert.Equal(entries.OrderBy(e => e.Key), back.OrderBy(e => e.Key));
    }

    [Fact]
    public void V170SettingsWithoutTimesReadAsTheOldest()
    {
        var entries = AccountAlias.FromSettings(
            new Dictionary<string, string> { ["a"] = " 개인 ", ["b"] = "  ", [" "] = "이름" }, null);

        Assert.Equal(new AccountAliasEntry("개인", DateTimeOffset.MinValue), Assert.Single(entries).Value);
        Assert.Empty(AccountAlias.FromSettings(null, null));
    }

    [Theory]
    [InlineData("회사", "김민준 · Acme", "회사")]
    [InlineData("", "김민준 · Acme", "김민준 · Acme")]
    [InlineData(null, " 김민준 ", "김민준")]
    [InlineData(null, null, "")]
    public void DisplayPrefersTheAlias(string? alias, string? label, string expected) =>
        Assert.Equal(expected, AccountAlias.Display(alias, label));
}
