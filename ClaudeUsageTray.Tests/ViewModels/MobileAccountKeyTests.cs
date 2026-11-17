using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

// 휴대폰 계정 묶기 키(#220): 같은 Team 조직(orgUuid 공유)의 다른 사람을 한 계정으로 합치지 않는다.
public class MobileAccountKeyTests
{
    [Fact]
    public void AddsAccountUuidSoTeamMembersStaySeparate()
    {
        Assert.Equal("org|me", MainViewModel.BuildClaudeMobileAccountKey("org", "me"));
        Assert.NotEqual(
            MainViewModel.BuildClaudeMobileAccountKey("org", "me"),
            MainViewModel.BuildClaudeMobileAccountKey("org", "colleague"));
    }

    [Fact]
    public void FallsBackToOrgOnlyAndToUnknownWithoutLogin()
    {
        Assert.Equal("org", MainViewModel.BuildClaudeMobileAccountKey(" org ", null));
        Assert.Null(MainViewModel.BuildClaudeMobileAccountKey(null, "me"));
    }

    // #275: 최근 Claude Code 는 자격 파일에 organizationUuid 를 쓰지 않는다 — 그러면 ~/.claude.json 의 값을 쓴다.
    [Fact]
    public void OrgUuidFallsBackToProfileWhenCredentialsLackIt()
    {
        Assert.Equal("org-profile", MainViewModel.ResolveClaudeOrgUuid(null, hasCredentials: true, " org-profile "));
        Assert.Equal("org-profile", MainViewModel.ResolveClaudeOrgUuid("  ", hasCredentials: true, "org-profile"));
    }

    [Fact]
    public void OrgUuidPrefersCredentialsSoExistingHistoryKeepsItsScope()
        => Assert.Equal("org-cred", MainViewModel.ResolveClaudeOrgUuid("org-cred", hasCredentials: true, "org-profile"));

    [Fact]
    public void OrgUuidIsUnknownWhenLoggedOutOrNowhere()
    {
        // 로그아웃(자격 파일 없음)이면 프로필에 예전 계정이 남아 있어도 계정을 가진 척하지 않는다.
        Assert.Null(MainViewModel.ResolveClaudeOrgUuid(null, hasCredentials: false, "org-profile"));
        Assert.Null(MainViewModel.ResolveClaudeOrgUuid(null, hasCredentials: true, null));
        Assert.Null(MainViewModel.ResolveClaudeOrgUuid(null, hasCredentials: true, " "));
    }
}
