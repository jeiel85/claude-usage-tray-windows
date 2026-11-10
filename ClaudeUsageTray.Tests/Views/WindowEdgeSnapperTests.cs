using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ClaudeUsageTray.Tests.ViewModels;
using ClaudeUsageTray.Views;
using Xunit;

namespace ClaudeUsageTray.Tests.Views;

/// <summary>
/// 실제 창 핸들에 WM_MOVING 을 보내 끄는 동안의 가장자리 붙기가 창에 걸리는지 본다 (#214).
/// 마우스를 움직이지 않도록 이동 루프 대신 메시지를 직접 보낸다. 창은 띄우지 않고 핸들만 만든다.
/// </summary>
public class WindowEdgeSnapperTests
{
    private const int WM_MOVING = 0x0216;

    [Fact]
    public async Task WmMoving_NearWorkAreaEdge_AttachesWithGap()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var (window, hwnd) = CreateWindow();
            using var snapper = new WindowEdgeSnapper(window, edgeGapDip: 8);
            try
            {
                var area = PrimaryWorkArea();
                var gap = (int)Math.Round(8 * VisualTreeHelper.GetDpi(window).DpiScaleX);
                var rect = new TestRect(area.Left + 1, area.Top + 200, area.Left + 201, area.Top + 320);

                var result = SendMessage(hwnd, WM_MOVING, IntPtr.Zero, ref rect);

                Assert.Equal(new IntPtr(1), result);
                Assert.Equal(area.Left + gap, rect.Left);
                Assert.Equal(200, rect.Right - rect.Left);
                Assert.Equal(area.Top + 200, rect.Top);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task WmMoving_FarFromEdges_LeavesProposedRect()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var (window, hwnd) = CreateWindow();
            using var snapper = new WindowEdgeSnapper(window, edgeGapDip: 8);
            try
            {
                var area = PrimaryWorkArea();
                var original = new TestRect(area.Left + 100, area.Top + 100, area.Left + 300, area.Top + 220);
                var rect = original;

                var result = SendMessage(hwnd, WM_MOVING, IntPtr.Zero, ref rect);

                Assert.Equal(IntPtr.Zero, result);
                Assert.Equal(original, rect);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task WmMoving_AfterDispose_IsNotHandled()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var (window, hwnd) = CreateWindow();
            var snapper = new WindowEdgeSnapper(window, edgeGapDip: 0);
            try
            {
                snapper.Dispose();
                var area = PrimaryWorkArea();
                var original = new TestRect(area.Left + 5, area.Top + 100, area.Left + 205, area.Top + 220);
                var rect = original;

                SendMessage(hwnd, WM_MOVING, IntPtr.Zero, ref rect);

                Assert.Equal(original, rect);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task KeepInWorkArea_WindowAboveTopEdge_PullsItBackInsideTheGap()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var (window, hwnd) = CreateWindow();
            using var snapper = new WindowEdgeSnapper(window, edgeGapDip: 8);
            try
            {
                var area = PrimaryWorkArea();
                var gap = (int)Math.Round(8 * VisualTreeHelper.GetDpi(window).DpiScaleX);
                SetWindowPos(hwnd, IntPtr.Zero, area.Left + 100, area.Top - 30, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

                snapper.KeepInWorkArea();

                Assert.True(GetWindowRect(hwnd, out var rect));
                Assert.Equal(area.Top + gap, rect.Top);
                Assert.Equal(area.Left + 100, rect.Left);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task CurrentWorkAreaHeight_ReturnsMonitorWorkAreaInDip()
    {
        await WpfTestHost.RunAsync(() =>
        {
            var (window, hwnd) = CreateWindow();
            using var snapper = new WindowEdgeSnapper(window, edgeGapDip: 8);
            try
            {
                var area = PrimaryWorkArea();
                SetWindowPos(hwnd, IntPtr.Zero, area.Left + 100, area.Top + 100, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                var expected = (area.Bottom - area.Top) / VisualTreeHelper.GetDpi(window).DpiScaleY;

                Assert.Equal(expected, snapper.CurrentWorkAreaHeight());
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static (Window Window, IntPtr Hwnd) CreateWindow()
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Width = 200,
            Height = 120,
        };
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        return (window, hwnd);
    }

    /// <summary>(0,0) 은 언제나 주 모니터 위다.</summary>
    private static PixelRect PrimaryWorkArea()
    {
        Assert.True(WindowEdgeSnapper.TryGetWorkArea(new PixelRect(0, 0, 1, 1), out var area));
        return area;
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct TestRect(int Left, int Top, int Right, int Bottom);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref TestRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out TestRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
