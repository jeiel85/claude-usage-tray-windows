using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

// 문구 단정은 프로세스 전역 Loc 언어에 기대므로 Loc 을 건드리는 다른 테스트와 직렬화한다.
[Collection("WpfTests")]
public class ClaudeViewModelLoginActionTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public void ShowLoginAction_RequiresErrorAndNeedsLogin(bool hasError, bool needsLogin, bool expected)
    {
        var vm = new ClaudeViewModel { HasError = hasError, NeedsLogin = needsLogin };

        Assert.Equal(expected, vm.ShowLoginAction);
    }

    // 에러를 지우는 경로(성공·동기화 값 적용 등)가 NeedsLogin 을 따로 내리지 않아도 버튼이 남으면 안 된다.
    [Fact]
    public void ClearingError_HidesLoginAction_AndClearsLaunchError()
    {
        var vm = new ClaudeViewModel { HasError = true, NeedsLogin = true, LoginLaunchError = "boom" };
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.HasError = false;

        Assert.False(vm.ShowLoginAction);
        Assert.Equal("", vm.LoginLaunchError);
        Assert.Contains(nameof(ClaudeViewModel.ShowLoginAction), changed);
    }

    [Fact]
    public void LoginActionLabel_SwitchesToInstall_WhenCliIsMissing()
    {
        var originalLang = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("ko");
            var vm = new ClaudeViewModel();
            Assert.True(vm.IsCliInstalled);
            Assert.Equal(Loc.ClaudeLoginInTerminal, vm.LoginActionLabel);
            Assert.Equal(Loc.ClaudeLoginInTerminalTooltip, vm.LoginActionTooltip);

            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.IsCliInstalled = false;

            Assert.Equal(Loc.ClaudeInstallAndLogin, vm.LoginActionLabel);
            Assert.Equal(Loc.ClaudeInstallAndLoginTooltip, vm.LoginActionTooltip);
            Assert.Contains(nameof(ClaudeViewModel.LoginActionLabel), changed);
        }
        finally
        {
            Loc.SetLanguage(originalLang);
        }
    }
}
