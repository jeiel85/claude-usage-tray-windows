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
/// <para>
/// 목록(<c>ItemsControl</c>) 행은 새로고침 때 항목이 새로 만들어져 막대도 새것이 된다. 그런 막대는
/// <c>views:GaugeAnimation.Key</c> 에 행마다 다른 이름을 주면, 같은 이름의 막대가 마지막으로 보였던 길이에서
/// 출발한다(#325). 이름은 목록마다 앞말을 붙여 다른 목록과 겹치지 않게 한다.
/// </para>
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

    /// <summary>다시 만들어지는 목록 행 막대의 이름. 같은 이름의 막대가 마지막으로 보였던 길이를 이어 받는다.</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(GaugeAnimation), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);

    public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

    // 이름 → 그 이름의 막대가 보이는 동안 마지막으로 정해진 값(UI 스레드에서만 쓴다).
    // 숨을 때가 아니라 보이는 동안 기록한다 — 행을 바꿀 때 옛 막대의 '숨음' 알림이 새 막대가 보인 뒤에 와서
    // 숨을 때 기록하면 새 막대가 이어 받지 못했다(실측). 항목은 목록 행 수(한도·창 몇 개)만큼이라 따로 비우지 않는다.
    private static readonly Dictionary<string, double> LastShownByKey = new(StringComparer.Ordinal);

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
        if (first)
        {
            // 새로 만든 목록 행 막대: 같은 이름의 막대가 보였던 길이가 있으면 거기서 출발한다.
            SeedFromKey(bar);
            var lastShown = (double)bar.GetValue(LastShownProperty);
            if (bar.IsVisible && AnimationsEnabled && !double.IsNaN(lastShown))
            {
                Animate(bar, lastShown, target);
                return;
            }
        }

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

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 템플릿 안에서는 값이 이름보다 먼저 묶인다(실측) — 이름이 들어온 이 때 이어 받고, 지금 값을 기록한다.
        if (d is not RangeBase bar) return;

        var seeded = !double.IsNaN((double)bar.GetValue(LastShownProperty));
        SeedFromKey(bar);
        var target = Coerce(bar, GetValue(bar));
        if (double.IsNaN(target)) return;

        var lastShown = (double)bar.GetValue(LastShownProperty);
        if (!seeded && !double.IsNaN(lastShown) && bar.IsVisible && AnimationsEnabled)
        {
            Animate(bar, lastShown, target);
            return;
        }
        RememberShown(bar, target);
    }

    private static void SeedFromKey(RangeBase bar)
    {
        if (!double.IsNaN((double)bar.GetValue(LastShownProperty))) return;
        if (GetKey(bar) is { Length: > 0 } key && LastShownByKey.TryGetValue(key, out var remembered))
            bar.SetValue(LastShownProperty, remembered);
    }

    /// <summary>테스트용: 이름별 기록을 비운다.</summary>
    internal static void ForgetKeys() => LastShownByKey.Clear();

    private static void Animate(RangeBase bar, double from, double to)
    {
        from = Coerce(bar, from);
        to = Coerce(bar, to);
        if (NearlyEqual(from, to, bar))
        {
            SetInstant(bar, to);
            return;
        }

        RememberShown(bar, to);
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
        RememberShown(bar, value);
    }

    private static void RememberShown(RangeBase bar, double value)
    {
        if (bar.IsVisible && GetKey(bar) is { Length: > 0 } key) LastShownByKey[key] = value;
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
