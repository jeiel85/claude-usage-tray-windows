using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>창을 작업 영역 가장자리에 붙이는 계산 (#214, #216).</summary>
public class EdgeSnapTests
{
    // 1920×1080 모니터, 아래 48px 작업표시줄
    private static readonly PixelRect Area = new(0, 0, 1920, 1032);
    // 트레이 모서리 기본 위치와 같은 8px 간격을 둔 붙는 영역
    private static readonly PixelRect GapArea = Area.Deflate(8);
    private const int Threshold = 20;

    private static PixelRect Window(int left, int top, int width = 400, int height = 300) =>
        new(left, top, left + width, top + height);

    [Fact]
    public void Deflate_ShrinksEverySide()
    {
        Assert.Equal(new PixelRect(8, 8, 1912, 1024), GapArea);
    }

    [Fact]
    public void Snap_NearLeftAndTop_AttachesToBoth()
    {
        var snapped = EdgeSnap.Snap(Window(15, 12), Area, Threshold);

        Assert.Equal(Window(0, 0), snapped);
    }

    [Fact]
    public void Snap_NearRightAndBottom_AttachesAboveTaskbar()
    {
        // 오른쪽 끝 1910, 아래 끝 1020 — 각각 10·12px 모자람
        var snapped = EdgeSnap.Snap(Window(1510, 720), Area, Threshold);

        Assert.Equal(Window(1520, 732), snapped);
    }

    [Fact]
    public void Snap_SlightlyPastEdge_PullsBackToEdge()
    {
        var snapped = EdgeSnap.Snap(Window(-12, 740), Area, Threshold);

        Assert.Equal(Window(0, 732), snapped);
    }

    [Fact]
    public void Snap_FartherThanThreshold_LeavesWindowWhereItIs()
    {
        var window = Window(21, 500);

        Assert.Equal(window, EdgeSnap.Snap(window, Area, Threshold));
    }

    [Fact]
    public void Snap_WithGap_StopsShortOfTheEdge()
    {
        var snapped = EdgeSnap.Snap(Window(14, 400), GapArea, Threshold);

        Assert.Equal(Window(8, 400), snapped);
    }

    [Fact]
    public void Snap_WithGap_PushedFlushAgainstCorner_LandsOnDefaultTrayPosition()
    {
        // 오른쪽 아래 끝에 딱 붙도록 끈 창 → 트레이 모서리 기본 위치(가장자리에서 8px)로 붙는다
        var snapped = EdgeSnap.Snap(Window(1520, 732), GapArea, Threshold);

        Assert.Equal(Window(1920 - 8 - 400, 1032 - 8 - 300), snapped);
    }

    [Fact]
    public void Snap_WithGap_DefaultTrayPositionStaysPut()
    {
        var window = Window(1920 - 8 - 400, 1032 - 8 - 300);

        Assert.Equal(window, EdgeSnap.Snap(window, GapArea, Threshold));
    }

    [Fact]
    public void Snap_AreaNarrowerThanTwoThresholds_PicksNearerEdge()
    {
        var narrow = new PixelRect(0, 0, 420, 1032);

        Assert.Equal(Window(0, 500), EdgeSnap.Snap(Window(5, 500), narrow, Threshold));
        Assert.Equal(Window(20, 500), EdgeSnap.Snap(Window(15, 500), narrow, Threshold));
    }

    [Fact]
    public void Snap_SecondMonitorOnLeft_UsesItsCoordinates()
    {
        var left = new PixelRect(-1920, 0, 0, 1032).Deflate(8);

        var snapped = EdgeSnap.Snap(Window(-1905, 300), left, Threshold);

        Assert.Equal(Window(-1912, 300), snapped);
    }

    [Fact]
    public void Clamp_OffRightAndBottom_PullsInside()
    {
        var clamped = EdgeSnap.Clamp(Window(1700, 900), Area);

        Assert.Equal(Window(1520, 732), clamped);
    }

    [Fact]
    public void Clamp_InsideArea_LeavesWindowWhereItIs()
    {
        var window = Window(300, 300);

        Assert.Equal(window, EdgeSnap.Clamp(window, Area));
    }

    [Fact]
    public void Clamp_WithGap_KeepsTheGap()
    {
        Assert.Equal(Window(8, 8), EdgeSnap.Clamp(Window(-30, -40), GapArea));
        Assert.Equal(Window(1512, 724), EdgeSnap.Clamp(Window(1600, 732), GapArea));
    }

    [Fact]
    public void Clamp_TallerThanArea_AlignsTop()
    {
        var clamped = EdgeSnap.Clamp(Window(100, 50, height: 1200), Area);

        Assert.Equal(Window(100, 0, height: 1200), clamped);
    }

    [Fact]
    public void KeepAttached_BottomAttachedWindowGrows_StaysOnBottomEdge()
    {
        var before = Window(800, 732, height: 300);
        var after = Window(800, 732, height: 380);

        var kept = EdgeSnap.KeepAttached(before, after, Area);

        Assert.Equal(Window(800, 652, height: 380), kept);
    }

    [Fact]
    public void KeepAttached_BottomAttachedWindowShrinks_StaysOnBottomEdge()
    {
        var before = Window(800, 732, height: 300);
        var after = Window(800, 732, height: 200);

        var kept = EdgeSnap.KeepAttached(before, after, Area);

        Assert.Equal(Window(800, 832, height: 200), kept);
    }

    [Fact]
    public void KeepAttached_OffByRoundingPixel_StillCountsAsAttached()
    {
        var before = Window(800, 731, height: 300); // 아래 끝 1031 — 반올림 1px
        var after = Window(800, 731, height: 200);

        var kept = EdgeSnap.KeepAttached(before, after, Area);

        Assert.Equal(1032, kept.Bottom);
    }

    [Fact]
    public void KeepAttached_NotAttached_LeavesWindowWhereItIs()
    {
        var before = Window(800, 500, height: 300);
        var after = Window(800, 500, height: 380);

        Assert.Equal(after, EdgeSnap.KeepAttached(before, after, Area));
    }

    [Fact]
    public void KeepAttached_TopAttached_KeepsTop()
    {
        var before = Window(800, 0, height: 300);
        var after = Window(800, 0, height: 380);

        Assert.Equal(after, EdgeSnap.KeepAttached(before, after, Area));
    }

    [Fact]
    public void KeepAttached_WithGap_KeepsTheGapAboveTaskbar()
    {
        // 아래 끝 1024 — 작업표시줄에서 8px 띄워 붙어 있던 창
        var before = Window(800, 724, height: 300);
        var after = Window(800, 724, height: 250);

        var kept = EdgeSnap.KeepAttached(before, after, GapArea);

        Assert.Equal(Window(800, 774, height: 250), kept);
    }
}
