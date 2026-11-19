using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Claude 계정 별명(#307): 계정마다 따로 붙고, 알림 제목 한 줄에 들어가게 정리된다.
/// </summary>
public class AccountAliasTests
{
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
        var aliases = AccountAlias.With(null, "personal-org|acct", "개인");
        aliases = AccountAlias.With(aliases, "team-org|acct", "회사");

        Assert.Equal("개인", AccountAlias.Find(aliases, "personal-org|acct"));
        Assert.Equal("회사", AccountAlias.Find(aliases, "team-org|acct"));
        Assert.Equal("", AccountAlias.Find(aliases, "other-org|acct"));
        Assert.Equal("", AccountAlias.Find(aliases, null));
    }

    [Fact]
    public void AnEmptyAliasRemovesOnlyThatAccount()
    {
        var aliases = AccountAlias.With(AccountAlias.With(null, "a", "개인"), "b", "회사");

        var next = AccountAlias.With(aliases, "a", "  ");

        Assert.False(next.ContainsKey("a"));
        Assert.Equal("회사", AccountAlias.Find(next, "b"));
        // 원래 사전은 그대로다 — 백그라운드에서 읽는 쪽이 반쯤 바뀐 사전을 보지 않게 새 사전을 만든다.
        Assert.Equal("개인", AccountAlias.Find(aliases, "a"));
    }

    [Fact]
    public void WithoutAnAccountKeyNothingIsAdded()
    {
        var aliases = AccountAlias.With(null, null, "회사");
        Assert.Empty(aliases);
    }

    [Fact]
    public void NormalizeDropsBlankEntriesFromTheFile()
    {
        var aliases = AccountAlias.Normalize(new Dictionary<string, string>
        {
            ["a"] = " 개인 ",
            ["b"] = "  ",
            [" "] = "이름",
        });

        Assert.Equal(new Dictionary<string, string> { ["a"] = "개인" }, aliases);
        Assert.Empty(AccountAlias.Normalize(null));
    }

    [Theory]
    [InlineData("회사", "김민준 · Acme", "회사")]
    [InlineData("", "김민준 · Acme", "김민준 · Acme")]
    [InlineData(null, " 김민준 ", "김민준")]
    [InlineData(null, null, "")]
    public void DisplayPrefersTheAlias(string? alias, string? label, string expected) =>
        Assert.Equal(expected, AccountAlias.Display(alias, label));
}
