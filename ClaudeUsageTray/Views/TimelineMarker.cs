using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Size = System.Windows.Size;

namespace ClaudeUsageTray.Views;

/// <summary>
/// 게이지 막대 위에 겹치는 시간선 마커(#237) — 창이 지난 비율(<see cref="Percent"/>) 위치에 세로선 하나.
/// 팝업의 모든 게이지가 같은 모양(너비 1.5 · 높이 8 · <c>#E2E8F0</c> · 모서리 1, <c>docs/UI_GUIDELINES.md</c> 3절)이라
/// 열 두 개짜리 Grid 를 게이지마다 복사하던 것을 이 요소 하나로 쓴다. 선의 오른쪽 끝이 <c>너비 × 비율</c> 에 오고
/// 요소 밖은 그리지 않는다 — 비율 0 이면 보이지 않는다(예전 Grid 와 같다). 숨김은 쓰는 쪽이 <c>Visibility</c> 로 한다.
/// </summary>
public sealed class TimelineMarker : FrameworkElement
{
    public const double LineWidth = 1.5;
    public const double LineHeight = 8;

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(TimelineMarker),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush LineBrush = CreateLineBrush();

    /// <summary>창이 지난 비율(0~1). 범위 밖·NaN 은 0~1 로 맞춘다.</summary>
    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public TimelineMarker()
    {
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Input : 요소 너비·높이, 비율
    /// Output: 선을 그릴 사각형(요소 안으로 자른 것). 그릴 것이 없으면 <see cref="Rect.Empty"/>
    /// </summary>
    internal static Rect LineRect(double width, double height, double percent)
    {
        if (width <= 0 || height <= 0) return Rect.Empty;
        var p = double.IsNaN(percent) ? 0 : Math.Clamp(percent, 0, 1);
        var right = width * p;
        var left = Math.Max(0, right - LineWidth);
        return right - left <= 0 ? Rect.Empty : new Rect(left, 0, right - left, height);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(0, double.IsInfinity(availableSize.Height) ? LineHeight : Math.Min(LineHeight, availableSize.Height));

    protected override void OnRender(DrawingContext drawingContext)
    {
        var rect = LineRect(ActualWidth, ActualHeight, Percent);
        if (!rect.IsEmpty) drawingContext.DrawRoundedRectangle(LineBrush, null, rect, 1, 1);
    }

    private static Brush CreateLineBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
        brush.Freeze();
        return brush;
    }
}
