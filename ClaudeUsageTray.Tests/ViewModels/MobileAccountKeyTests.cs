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
}
