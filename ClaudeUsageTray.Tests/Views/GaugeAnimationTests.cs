using System.Windows;
using System.Windows.Controls;
using ClaudeUsageTray.Tests.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace ClaudeUsageTray.Tests.Views;

// 게이지 막대 차오름 애니메이션(#322). 실제 창에 막대를 띄워 보이는 동안·숨은 동안·처음 값의 동작을 본다.
[Collection("WpfTests")]
[Trait("Category", "Integration")]
public class GaugeAnimationTests
{
    private static (Window window, ProgressBar bar) Host(double initial)
    {
        var bar = new ProgressBar { Maximum = 1, Width = 200, Height = 4 };
        GaugeAnimation.SetValue(bar, initial);
        var window = new Window
        {
            Content = bar, Width = 240, Height = 80, Left = -10000, Top = -10000,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        return (window, bar);
    }

    // GetValueSource().IsAnimated 는 첫 틱 전에는 false 라, 애니메이션이 붙었는지는 HasAnimatedProperties 로 본다
    // (이 막대에서 애니메이션되는 속성은 Value 뿐이다).
    private static bool IsAnimated(ProgressBar bar) => bar.HasAnimatedProperties;

    private static async Task RunWithAnimations(bool enabled, Func<Task> body)
    {
        await WpfTestHost.RunAsync(async () =>
        {
            GaugeAnimation.AnimationsEnabledOverride = enabled;
            try { await body(); }
            finally { GaugeAnimation.AnimationsEnabledOverride = null; }
        });
    }

    [Fact]
    public async Task FirstValue_IsAppliedWithoutAnimation()
    {
        await RunWithAnimations(true, () =>
        {
            var (window, bar) = Host(0.4);
            window.Show();
            try
            {
                Assert.Equal(0.4, bar.Value, 6);
                Assert.False(IsAnimated(bar));
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ChangeWhileVisible_FillsFromOldToNewAndSettles()
    {
        await RunWithAnimations(true, async () =>
        {
            var (window, bar) = Host(0.2);
            window.Show();
            try
            {
                GaugeAnimation.SetValue(bar, 0.8);

                Assert.True(IsAnimated(bar));
                Assert.Equal(0.2, bar.Value, 6);   // 첫 틱 전에는 출발점 — 새 길이가 먼저 비치지 않는다

                await Task.Delay(200);
                Assert.InRange(bar.Value, 0.2 + 1e-3, 0.8 - 1e-3);   // 중간 지점을 지나간다

                await Task.Delay(GaugeAnimation.FillDuration.TimeSpan + TimeSpan.FromMilliseconds(400));

                Assert.Equal(0.8, bar.Value, 6);
                Assert.False(IsAnimated(bar));   // 도착하면 애니메이션을 걷어 내 다음 값이 가려지지 않는다
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ChangeWhileHidden_AnimatesFromLastShownWhenShownAgain()
    {
        await RunWithAnimations(true, async () =>
        {
            var (window, bar) = Host(0.3);
            window.Show();
            try
            {
                window.Hide();
                GaugeAnimation.SetValue(bar, 0.9);

                // 숨어 있는 동안은 바로 적용된다(값을 읽는 쪽·렌더 스냅샷이 옛 값을 보지 않게).
                Assert.Equal(0.9, bar.Value, 6);
                Assert.False(IsAnimated(bar));

                window.Show();
                Assert.True(IsAnimated(bar));
                Assert.True(bar.Value < 0.9 - 1e-3, $"보일 때 0.3 에서 출발해야 한다: {bar.Value}");

                await Task.Delay(GaugeAnimation.FillDuration.TimeSpan + TimeSpan.FromMilliseconds(400));
                Assert.Equal(0.9, bar.Value, 6);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task ShownAgainWithoutChange_DoesNotAnimate()
    {
        await RunWithAnimations(true, () =>
        {
            var (window, bar) = Host(0.5);
            window.Show();
            try
            {
                window.Hide();
                window.Show();
                Assert.False(IsAnimated(bar));
                Assert.Equal(0.5, bar.Value, 6);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AnimationsOff_AppliesImmediately()
    {
        await RunWithAnimations(false, () =>
        {
            var (window, bar) = Host(0.1);
            window.Show();
            try
            {
                GaugeAnimation.SetValue(bar, 0.7);
                Assert.False(IsAnimated(bar));
                Assert.Equal(0.7, bar.Value, 6);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task NewValueMidAnimation_EndsAtLatestValue()
    {
        await RunWithAnimations(true, async () =>
        {
            var (window, bar) = Host(0.0);
            window.Show();
            try
            {
                GaugeAnimation.SetValue(bar, 1.0);
                await Task.Delay(150);
                GaugeAnimation.SetValue(bar, 0.4);

                await Task.Delay(GaugeAnimation.FillDuration.TimeSpan + TimeSpan.FromMilliseconds(400));
                Assert.Equal(0.4, bar.Value, 6);
                Assert.False(IsAnimated(bar));
            }
            finally { window.Close(); }
        });
    }
}
