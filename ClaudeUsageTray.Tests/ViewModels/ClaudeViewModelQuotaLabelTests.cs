using System.Collections.Generic;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

public class ClaudeViewModelQuotaLabelTests
{
    /// <summary>
    /// 회귀 방지: 조회에 한 번도 성공하지 못한 상태에서 퍼센트 0 을 "0%"로 그리면
    /// "여유 100%"라는 반대 의미가 전달된다. 이 상태는 "—"여야 한다.
    /// </summary>
    [Fact]
    public void PercentLabels_ShowDash_UntilQuotaHasBeenFetched()
    {
        var vm = new ClaudeViewModel();

        Assert.False(vm.HasQuotaData);
        Assert.Equal("—", vm.ShortPercentLabel);
        Assert.Equal("—", vm.LongPercentLabel);
    }

    [Fact]
    public void PercentLabels_ShowPercent_OnceQuotaIsKnown()
    {
        var vm = new ClaudeViewModel { HasQuotaData = true, ShortPercent = 0.37, LongPercent = 0.05 };

        Assert.Equal(0.37.ToString("P0"), vm.ShortPercentLabel);
        Assert.Equal(0.05.ToString("P0"), vm.LongPercentLabel);
    }

    /// <summary>실제 0% 사용은 "—"가 아니라 "0%"로 나와야 한다 (미조회와 구분).</summary>
    [Fact]
    public void PercentLabels_ShowZeroPercent_WhenQuotaIsKnownAndZero()
    {
        var vm = new ClaudeViewModel { HasQuotaData = true, ShortPercent = 0 };

        Assert.Equal(0d.ToString("P0"), vm.ShortPercentLabel);
    }

    [Fact]
    public void PercentLabels_RaiseChangeNotifications()
    {
        var vm = new ClaudeViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ShortPercent = 0.5;
        vm.LongPercent = 0.2;
        vm.HasQuotaData = true;

        Assert.Contains(nameof(ClaudeViewModel.ShortPercentLabel), changed);
        Assert.Contains(nameof(ClaudeViewModel.LongPercentLabel), changed);
        // HasQuotaData 전환 하나로 두 라벨이 모두 갱신되어야 한다
        Assert.Equal(2, changed.FindAll(p =>
            p == nameof(ClaudeViewModel.ShortPercentLabel)).Count);
    }

    [Fact]
    public void UsageSummaryUnknown_IsNotAZeroPercentClaim()
    {
        Assert.False(string.IsNullOrWhiteSpace(Loc.UsageSummaryUnknown));
        Assert.DoesNotContain("0%", Loc.UsageSummaryUnknown);
    }
}
