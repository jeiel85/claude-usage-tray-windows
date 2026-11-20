using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ClaudeUsageTray.ViewModels;
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

                // 중간 지점을 지나간다. 한 번(200ms 뒤) 재면 느린 CI 러너에서 그 사이 채우기가 끝나 버려 0.8 이 나왔다(PR #335) —
                // 짧게 여러 번 재서 출발점과 도착점 사이 값을 한 번이라도 봤는지 본다.
                var deadline = DateTime.UtcNow + GaugeAnimation.FillDuration.TimeSpan + TimeSpan.FromMilliseconds(400);
                var sawMidway = false;
                while (DateTime.UtcNow < deadline && !sawMidway)
                {
                    await Task.Delay(15);
                    sawMidway = bar.Value > 0.2 + 1e-3 && bar.Value < 0.8 - 1e-3;
                }
                Assert.True(sawMidway, "the bar jumped to the new length instead of filling");

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

    // 목록 행(#325): 새로고침이 항목을 새로 만들면 막대도 새것이다. 같은 이름의 막대가 보였던 길이에서 출발해야 한다.
    private const string RowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:views="clr-namespace:ClaudeUsageTray.Views;assembly=ClaudeUsageTray">
            <ProgressBar Maximum="1" Width="200" Height="4"
                         views:GaugeAnimation.Key="{Binding AnimationKey, StringFormat='test:{0}'}"
                         views:GaugeAnimation.Value="{Binding UsagePercent}"/>
        </DataTemplate>
        """;

    private static (Window window, ItemsControl list) HostList(params QuotaGaugeRow[] rows)
    {
        var list = new ItemsControl
        {
            ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate),
            ItemsSource = rows,
        };
        var window = new Window
        {
            Content = list, Width = 240, Height = 120, Left = -10000, Top = -10000,
            ShowInTaskbar = false, WindowStyle = WindowStyle.None,
        };
        return (window, list);
    }

    private static QuotaGaugeRow Row(string key, double percent) =>
        new() { Key = key, UsagePercent = percent, Window = TimeSpan.FromDays(7) };

    private static ProgressBar BarAt(ItemsControl list, int index)
    {
        list.UpdateLayout();
        var container = (DependencyObject)list.ItemContainerGenerator.ContainerFromIndex(index);
        return FindBar(container) ?? throw new InvalidOperationException("막대를 찾지 못함");

        static ProgressBar? FindBar(DependencyObject node)
        {
            if (node is ProgressBar bar) return bar;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (FindBar(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
    }

    [Fact]
    public async Task RecreatedRowWhileVisible_FillsFromItsPreviousLength()
    {
        await RunWithAnimations(true, async () =>
        {
            GaugeAnimation.ForgetKeys();
            var (window, list) = HostList(Row("weekly", 0.2));
            window.Show();
            try
            {
                var oldBar = BarAt(list, 0);
                Assert.Equal(0.2, oldBar.Value, 6);

                list.ItemsSource = new[] { Row("weekly", 0.8) };
                var bar = BarAt(list, 0);

                Assert.NotSame(oldBar, bar);
                Assert.True(IsAnimated(bar));
                Assert.True(bar.Value < 0.8 - 1e-3, $"0.2 에서 출발해야 한다: {bar.Value}");

                await Task.Delay(GaugeAnimation.FillDuration.TimeSpan + TimeSpan.FromMilliseconds(400));
                Assert.Equal(0.8, bar.Value, 6);
                Assert.False(IsAnimated(bar));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task RecreatedRowWhileHidden_FillsWhenShownAgain()
    {
        await RunWithAnimations(true, () =>
        {
            GaugeAnimation.ForgetKeys();
            var (window, list) = HostList(Row("weekly", 0.3));
            window.Show();
            try
            {
                BarAt(list, 0);
                window.Hide();
                list.ItemsSource = new[] { Row("weekly", 0.9) };

                window.Show();
                var bar = BarAt(list, 0);
                Assert.True(IsAnimated(bar));
                Assert.True(bar.Value < 0.9 - 1e-3, $"0.3 에서 출발해야 한다: {bar.Value}");
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RowWithAnotherName_DoesNotInheritALength()
    {
        await RunWithAnimations(true, () =>
        {
            GaugeAnimation.ForgetKeys();
            var (window, list) = HostList(Row("weekly", 0.2));
            window.Show();
            try
            {
                BarAt(list, 0);
                list.ItemsSource = new[] { Row("five-hour", 0.8) };
                var bar = BarAt(list, 0);

                Assert.False(IsAnimated(bar));
                Assert.Equal(0.8, bar.Value, 6);
            }
            finally { window.Close(); }
            return Task.CompletedTask;
        });
    }
}
