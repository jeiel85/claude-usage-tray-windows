using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>창을 작업 영역 가장자리에 붙이는 계산 (#214).</summary>
public class EdgeSnapTests
{
    // 1920×1080 모니터, 아래 48px 작업표시줄
    private static readonly PixelRect Area = new(0, 0, 1920, 1032);
    private static readonly PixelInsets NoInset = new(0, 0, 0, 0);
    private static readonly PixelInsets ShadowInset = new(10, 10, 10, 10);
    private const int Threshold = 20;

    private static PixelRect Window(int left, int top, int width = 400, int height = 300) =>
        new(left, top, left + width, top + height);

    [Fact]
    public void Snap_NearLeftAndTop_AttachesToBoth()
    {
        var snapped = EdgeSnap.Snap(Window(15, 12), Area, NoInset, Threshold);

        Assert.Equal(Window(0, 0), snapped);
    }

    [Fact]
    public void Snap_NearRightAndBottom_AttachesAboveTaskbar()
    {
        // 오른쪽 끝 1910, 아래 끝 1020 — 각각 10·12px 모자람
        var snapped = EdgeSnap.Snap(Window(1510, 720), Area, NoInset, Threshold);

        Assert.Equal(Window(1520, 732), snapped);
    }

    [Fact]
    public void Snap_SlightlyPastEdge_PullsBackToEdge()
    {
        var snapped = EdgeSnap.Snap(Window(-12, 740), Area, NoInset, Threshold);

        Assert.Equal(Window(0, 732), snapped);
    }

    [Fact]
    public void Snap_FartherThanThreshold_LeavesWindowWhereItIs()
    {
        var window = Window(21, 500);

        Assert.Equal(window, EdgeSnap.Snap(window, Area, NoInset, Threshold));
    }

    [Fact]
    public void Snap_WithShadowInset_AttachesVisibleBorderNotWindowRect()
    {
        // 창 사각형은 왼쪽 0 이지만 보이는 테두리는 10 — 테두리를 0 에 맞추려면 창이 -10 으로 나간다
        var snapped = EdgeSnap.Snap(Window(0, 400), Area, ShadowInset, Threshold);

        Assert.Equal(Window(-10, 400), snapped);
    }

    [Fact]
    public void Snap_WithShadowInset_AttachesVisibleBottomToTaskbar()
    {
        var snapped = EdgeSnap.Snap(Window(800, 735), Area, ShadowInset, Threshold);

        Assert.Equal(Window(800, 742), snapped);
    }

    [Fact]
    public void Snap_AreaNarrowerThanTwoThresholds_PicksNearerEdge()
    {
        var narrow = new PixelRect(0, 0, 420, 1032);

        Assert.Equal(Window(0, 500), EdgeSnap.Snap(Window(5, 500), narrow, NoInset, Threshold));
        Assert.Equal(Window(20, 500), EdgeSnap.Snap(Window(15, 500), narrow, NoInset, Threshold));
    }

    [Fact]
    public void Snap_SecondMonitorOnLeft_UsesItsCoordinates()
    {
        var left = new PixelRect(-1920, 0, 0, 1032);

        var snapped = EdgeSnap.Snap(Window(-1905, 300), left, NoInset, Threshold);

        Assert.Equal(Window(-1920, 300), snapped);
    }

    [Fact]
    public void Clamp_OffRightAndBottom_PullsInside()
    {
        var clamped = EdgeSnap.Clamp(Window(1700, 900), Area, NoInset);

        Assert.Equal(Window(1520, 732), clamped);
    }

    [Fact]
    public void Clamp_InsideArea_LeavesWindowWhereItIs()
    {
        var window = Window(300, 300);

        Assert.Equal(window, EdgeSnap.Clamp(window, Area, NoInset));
    }

    [Fact]
    public void Clamp_WithShadowInset_LetsTransparentMarginHangOutside()
    {
        var window = Window(-10, -10);

        Assert.Equal(window, EdgeSnap.Clamp(window, Area, ShadowInset));
        Assert.Equal(Window(-10, -10), EdgeSnap.Clamp(Window(-30, -40), Area, ShadowInset));
    }

    [Fact]
    public void Clamp_TallerThanArea_AlignsTop()
    {
        var clamped = EdgeSnap.Clamp(Window(100, 50, height: 1200), Area, NoInset);

        Assert.Equal(Window(100, 0, height: 1200), clamped);
    }

    [Fact]
    public void KeepAttached_BottomAttachedWindowGrows_StaysOnBottomEdge()
    {
        var before = Window(800, 732, height: 300);
        var after = Window(800, 732, height: 380);

        var kept = EdgeSnap.KeepAttached(before, after, Area, NoInset);

        Assert.Equal(Window(800, 652, height: 380), kept);
    }

    [Fact]
    public void KeepAttached_BottomAttachedWindowShrinks_StaysOnBottomEdge()
    {
        var before = Window(800, 732, height: 300);
        var after = Window(800, 732, height: 200);

        var kept = EdgeSnap.KeepAttached(before, after, Area, NoInset);

        Assert.Equal(Window(800, 832, height: 200), kept);
    }

    [Fact]
    public void KeepAttached_OffByRoundingPixel_StillCountsAsAttached()
    {
        var before = Window(800, 731, height: 300); // 아래 끝 1031 — 반올림 1px
        var after = Window(800, 731, height: 200);

        var kept = EdgeSnap.KeepAttached(before, after, Area, NoInset);

        Assert.Equal(1032, kept.Bottom);
    }

    [Fact]
    public void KeepAttached_NotAttached_LeavesWindowWhereItIs()
    {
        var before = Window(800, 500, height: 300);
        var after = Window(800, 500, height: 380);

        Assert.Equal(after, EdgeSnap.KeepAttached(before, after, Area, NoInset));
    }

    [Fact]
    public void KeepAttached_TopAttached_KeepsTop()
    {
        var before = Window(800, 0, height: 300);
        var after = Window(800, 0, height: 380);

        Assert.Equal(after, EdgeSnap.KeepAttached(before, after, Area, NoInset));
    }

    [Fact]
    public void KeepAttached_WithShadowInset_UsesVisibleBorder()
    {
        // 보이는 아래 끝이 1032 — 창 사각형 아래 끝은 1042
        var before = Window(800, 742, height: 300);
        var after = Window(800, 742, height: 250);

        var kept = EdgeSnap.KeepAttached(before, after, Area, ShadowInset);

        Assert.Equal(Window(800, 792, height: 250), kept);
    }
}
