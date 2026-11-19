using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;

namespace ClaudeUsageTray.Views;

/// <summary>
/// 게이지 막대가 값이 바뀔 때 이전 길이에서 새 길이로 차오르게 한다(#322).
/// <c>Value="{Binding X}"</c> 대신 <c>views:GaugeAnimation.Value="{Binding X}"</c> 로 묶으면
/// 이 값이 바뀔 때 <see cref="RangeBase.Value"/> 를 애니메이션으로 옮긴다.
/// <list type="bullet">
/// <item>막대가 보이는 동안 바뀌면 지금 길이에서 새 길이로 움직인다.</item>
/// <item>숨어 있는 동안 바뀌면 바로 적용하고, 다음에 보일 때 마지막으로 보였던 길이에서 움직인다
///   (자동 새로고침은 대개 팝업이 닫혀 있을 때 돈다).</item>
/// <item>처음 받은 값, 한 번도 보인 적 없는 막대, Windows '애니메이션 효과'가 꺼진 경우는 움직이지 않는다.</item>
/// </list>
/// 페이스 오버레이처럼 겹친 막대는 같은 시간·곡선이라 함께 움직인다.
/// </summary>
public static class GaugeAnimation
{
    public static readonly Duration FillDuration = new(TimeSpan.FromMilliseconds(600));

    private static readonly IEasingFunction FillEasing = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static readonly DependencyProperty ValueProperty = DependencyProperty.RegisterAttached(
        "Value", typeof(double), typeof(GaugeAnimation),
        new PropertyMetadata(double.NaN, OnValueChanged));

    public static double GetValue(DependencyObject element) => (double)element.GetValue(ValueProperty);

    public static void SetValue(DependencyObject element, double value) => element.SetValue(ValueProperty, value);

    // 막대가 마지막으로 보였을 때의 값. 숨을 때 기록하고, 다시 보일 때 여기서 출발한다. NaN = 보인 적 없음.
    private static readonly DependencyProperty LastShownProperty = DependencyProperty.RegisterAttached(
        "LastShown", typeof(double), typeof(GaugeAnimation), new PropertyMetadata(double.NaN));

    // 시작한 애니메이션 번호. 새 애니메이션이 옛 것을 대신한 뒤 옛 것의 Completed 가 새 것을 끊지 않게 한다.
    private static readonly DependencyProperty GenerationProperty = DependencyProperty.RegisterAttached(
        "Generation", typeof(int), typeof(GaugeAnimation), new PropertyMetadata(0));

    private static readonly DependencyProperty HookedProperty = DependencyProperty.RegisterAttached(
        "Hooked", typeof(bool), typeof(GaugeAnimation), new PropertyMetadata(false));

    /// <summary>테스트에서 애니메이션 여부를 고정한다. null 이면 Windows 설정을 따른다.</summary>
    internal static bool? AnimationsEnabledOverride { get; set; }

    private static bool AnimationsEnabled => AnimationsEnabledOverride ?? SystemParameters.ClientAreaAnimation;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RangeBase bar) return;

        if (!(bool)bar.GetValue(HookedProperty))
        {
            bar.SetValue(HookedProperty, true);
            bar.IsVisibleChanged += OnIsVisibleChanged;
        }

        var target = (double)e.NewValue;
        if (double.IsNaN(target)) target = 0;

        var first = double.IsNaN((double)e.OldValue);
        if (first || !bar.IsVisible || !AnimationsEnabled)
        {
            SetInstant(bar, target);
            return;
        }

        Animate(bar, bar.Value, target);
    }

    private static void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var bar = (RangeBase)sender;
        var target = Coerce(bar, GetValue(bar));
        if (double.IsNaN(target)) return;

        if (!(bool)e.NewValue)
        {
            // 숨는 순간 보이던 길이(움직이던 중이면 그 위치가 아니라 도착점)를 기록하고 끝으로 맞춘다.
            bar.SetValue(LastShownProperty, target);
            SetInstant(bar, target);
            return;
        }

        var lastShown = (double)bar.GetValue(LastShownProperty);
        if (double.IsNaN(lastShown) || !AnimationsEnabled || NearlyEqual(lastShown, target, bar))
        {
            SetInstant(bar, target);
            return;
        }

        Animate(bar, lastShown, target);
    }

    private static void Animate(RangeBase bar, double from, double to)
    {
        from = Coerce(bar, from);
        to = Coerce(bar, to);
        if (NearlyEqual(from, to, bar))
        {
            SetInstant(bar, to);
            return;
        }

        var generation = (int)bar.GetValue(GenerationProperty) + 1;
        bar.SetValue(GenerationProperty, generation);

        var animation = new DoubleAnimation(from, to, FillDuration)
        {
            EasingFunction = FillEasing,
            FillBehavior = FillBehavior.HoldEnd,
        };
        // 도착하면 애니메이션을 걷어 내고 로컬 값으로 남긴다 — 다음 값이 HoldEnd 에 가려지지 않게.
        animation.Completed += (_, _) =>
        {
            if ((int)bar.GetValue(GenerationProperty) == generation) SetInstant(bar, to);
        };
        // 기준값은 출발점으로 둔다 — 첫 틱 전에 새 길이가 한 프레임 비치지 않게. 도착하면 Completed 가 새 값으로 바꾼다.
        // (SetCurrentValue 로 넣은 값은 애니메이션이 붙으면 기준값에서 사라지므로 로컬 값으로 둔다. Value 에는 바인딩이 없다.)
        bar.Value = from;
        bar.BeginAnimation(RangeBase.ValueProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void SetInstant(RangeBase bar, double value)
    {
        bar.SetValue(GenerationProperty, (int)bar.GetValue(GenerationProperty) + 1);
        bar.BeginAnimation(RangeBase.ValueProperty, null);
        bar.Value = value;
    }

    private static double Coerce(RangeBase bar, double value) =>
        double.IsNaN(value) ? value : Math.Clamp(value, bar.Minimum, Math.Max(bar.Minimum, bar.Maximum));

    private static bool NearlyEqual(double a, double b, RangeBase bar) =>
        Math.Abs(a - b) <= (bar.Maximum - bar.Minimum) * 1e-4;

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
