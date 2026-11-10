using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeUsageTray.Views;

/// <summary>물리 픽셀 사각형. Right·Bottom 은 Win32 RECT 처럼 끝을 포함하지 않는다.</summary>
internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);
}

/// <summary>창 사각형과 눈에 보이는 테두리 사이의 투명 여백 (물리 픽셀).</summary>
internal readonly record struct PixelInsets(int Left, int Top, int Right, int Bottom);

/// <summary>
/// 창을 작업 영역 가장자리에 맞추는 계산 (#214). 모두 물리 픽셀이고, 기준은 여백을 뺀 눈에 보이는 테두리다.
/// </summary>
internal static class EdgeSnap
{
    /// <summary>
    /// 크기가 바뀌기 전 가장자리에 붙어 있었는지 판단할 때 허용하는 오차. DIP 크기를 픽셀로 바꾸며 반올림하면
    /// 1px 쯤 어긋날 수 있다.
    /// </summary>
    internal const int AttachTolerance = 2;

    /// <summary>
    /// 테두리가 작업 영역 가장자리에서 <paramref name="threshold"/> 안이면 그 가장자리로 옮긴다. 크기는 그대로다.
    /// 가로·세로를 따로 본다 — 모서리 근처면 두 가장자리에 함께 붙는다.
    /// </summary>
    public static PixelRect Snap(PixelRect window, PixelRect area, PixelInsets inset, int threshold)
    {
        int dx = SnapOffset(window.Left + inset.Left, window.Right - inset.Right, area.Left, area.Right, threshold);
        int dy = SnapOffset(window.Top + inset.Top, window.Bottom - inset.Bottom, area.Top, area.Bottom, threshold);
        return window.Offset(dx, dy);
    }

    /// <summary>
    /// 테두리가 작업 영역 밖으로 나가 있으면 안으로 들인다. 작업 영역보다 크면 왼쪽·위를 맞춘다.
    /// </summary>
    public static PixelRect Clamp(PixelRect window, PixelRect area, PixelInsets inset)
    {
        int dx = ClampOffset(window.Left + inset.Left, window.Right - inset.Right, area.Left, area.Right);
        int dy = ClampOffset(window.Top + inset.Top, window.Bottom - inset.Bottom, area.Top, area.Bottom);
        return window.Offset(dx, dy);
    }

    /// <summary>
    /// 크기가 바뀌기 전(<paramref name="before"/>) 오른쪽·아래 가장자리에 붙어 있었으면 바뀐 뒤(<paramref name="after"/>)에도
    /// 그 가장자리에 남도록 옮긴다. WPF 는 창이 커지거나 줄 때 왼쪽·위를 두고 오른쪽·아래를 움직이므로,
    /// 왼쪽·위에 붙은 창은 손댈 필요가 없다. 양쪽에 다 붙어 있었으면(작업 영역만큼 큰 창) 왼쪽·위를 지킨다.
    /// </summary>
    public static PixelRect KeepAttached(PixelRect before, PixelRect after, PixelRect area, PixelInsets inset)
    {
        int dx = 0, dy = 0;

        if (IsNear(before.Right - inset.Right, area.Right) && !IsNear(before.Left + inset.Left, area.Left))
            dx = area.Right - (after.Right - inset.Right);
        if (IsNear(before.Bottom - inset.Bottom, area.Bottom) && !IsNear(before.Top + inset.Top, area.Top))
            dy = area.Bottom - (after.Bottom - inset.Bottom);

        return after.Offset(dx, dy);
    }

    private static int SnapOffset(int start, int end, int min, int max, int threshold)
    {
        int toStart = min - start;
        int toEnd = max - end;
        bool nearStart = Math.Abs(toStart) <= threshold;
        bool nearEnd = Math.Abs(toEnd) <= threshold;

        if (nearStart && nearEnd)
            return Math.Abs(toStart) <= Math.Abs(toEnd) ? toStart : toEnd;
        if (nearStart) return toStart;
        if (nearEnd) return toEnd;
        return 0;
    }

    private static int ClampOffset(int start, int end, int min, int max)
    {
        if (end - start >= max - min) return min - start;
        if (start < min) return min - start;
        if (end > max) return max - end;
        return 0;
    }

    private static bool IsNear(int value, int edge) => Math.Abs(value - edge) <= AttachTolerance;
}

/// <summary>
/// 창을 끄는 동안 창이 놓인 모니터의 작업 영역 가장자리(작업표시줄 경계 포함)에 자석처럼 붙인다 (#214).
/// DragMove 의 이동 루프가 보내는 WM_MOVING 에서 제안된 사각형을 고쳐 쓴다. 제안 사각형은 늘 커서 위치에서
/// 새로 계산되므로, 가장자리에서 더 멀리 끌면 붙었던 창이 저절로 떨어진다.
/// 창 사각형·작업 영역(GetMonitorInfo)을 같은 물리 픽셀로 다뤄 디스플레이 배율이 달라도 어긋나지 않는다.
/// </summary>
internal sealed class WindowEdgeSnapper : IDisposable
{
    /// <summary>가장자리에 붙기 시작하는 거리 (DIP). 배율에 맞춰 픽셀로 바꿔 쓴다.</summary>
    internal const double SnapDistanceDip = 16;

    private const int WM_MOVING = 0x0216;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly Window _window;
    private readonly Func<Thickness> _visualInset;
    private HwndSource? _source;
    private bool _disposed;

    /// <param name="window">대상 창.</param>
    /// <param name="visualInset">창 가장자리와 눈에 보이는 테두리 사이의 투명 여백 (DIP). 그림자 자리 등.</param>
    public WindowEdgeSnapper(Window window, Func<Thickness> visualInset)
    {
        _window = window;
        _visualInset = visualInset;

        if (FindSource() is { } source)
            Attach(source);
        else
            window.SourceInitialized += OnSourceInitialized;
    }

    /// <summary>테두리가 지금 모니터의 작업 영역 밖으로 나가 있으면 안으로 들인다.</summary>
    public void KeepInWorkArea()
    {
        if (!TryGetWindowRect(out var hwnd, out var rect) || !TryGetWorkArea(rect, out var area))
            return;

        var inset = InsetPixels(VisualTreeHelper.GetDpi(_window));
        MoveTo(hwnd, rect, EdgeSnap.Clamp(rect, area, inset));
    }

    /// <summary>
    /// 크기가 바뀐 뒤 호출한다. 바뀌기 전 붙어 있던 가장자리에 남기고, 작업 영역 밖으로 나간 만큼 들인다.
    /// 크기는 DIP 로 받는다 — SizeChanged 시점의 창 핸들 크기가 새 크기인지 장담할 수 없어 위치만 핸들에서 읽는다.
    /// </summary>
    public void KeepInWorkAreaAfterResize(System.Windows.Size previousSize, System.Windows.Size newSize)
    {
        if (!TryGetWindowRect(out var hwnd, out var rect))
            return;

        var dpi = VisualTreeHelper.GetDpi(_window);
        var before = WithSize(rect, previousSize, dpi);
        var after = WithSize(rect, newSize, dpi);
        if (!TryGetWorkArea(after, out var area))
            return;

        var inset = InsetPixels(dpi);
        MoveTo(hwnd, rect, EdgeSnap.Clamp(EdgeSnap.KeepAttached(before, after, area, inset), area, inset));
    }

    /// <summary>창이 지금 놓인 모니터의 작업 영역 높이 (DIP). 창 핸들이 없으면 null.</summary>
    public double? CurrentWorkAreaHeight()
    {
        if (!TryGetWindowRect(out _, out var rect) || !TryGetWorkArea(rect, out var area))
            return null;

        return (area.Bottom - area.Top) / VisualTreeHelper.GetDpi(_window).DpiScaleY;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _window.SourceInitialized -= OnSourceInitialized;
        _source?.RemoveHook(WndProc);
        _source = null;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _window.SourceInitialized -= OnSourceInitialized;
        if (!_disposed && FindSource() is { } source)
            Attach(source);
    }

    /// <summary>
    /// 창 핸들의 HwndSource. PresentationSource.FromVisual 은 핸들만 만들고 아직 띄우지 않은 창에서 null 을 줄 수 있어
    /// 핸들로 찾는다.
    /// </summary>
    private HwndSource? FindSource()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        return hwnd == IntPtr.Zero ? null : HwndSource.FromHwnd(hwnd);
    }

    private void Attach(HwndSource source)
    {
        _source = source;
        source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_MOVING || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var proposed = Marshal.PtrToStructure<NativeRect>(lParam).ToPixelRect();
        if (!TryGetWorkArea(proposed, out var area))
            return IntPtr.Zero;

        var dpi = VisualTreeHelper.GetDpi(_window);
        var threshold = (int)Math.Round(SnapDistanceDip * dpi.DpiScaleX);
        var snapped = EdgeSnap.Snap(proposed, area, InsetPixels(dpi), threshold);
        if (snapped == proposed)
            return IntPtr.Zero;

        Marshal.StructureToPtr(NativeRect.From(snapped), lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    private PixelInsets InsetPixels(DpiScale dpi)
    {
        var t = _visualInset();
        return new PixelInsets(
            (int)Math.Round(t.Left * dpi.DpiScaleX),
            (int)Math.Round(t.Top * dpi.DpiScaleY),
            (int)Math.Round(t.Right * dpi.DpiScaleX),
            (int)Math.Round(t.Bottom * dpi.DpiScaleY));
    }

    private static PixelRect WithSize(PixelRect rect, System.Windows.Size size, DpiScale dpi) => new(
        rect.Left,
        rect.Top,
        rect.Left + (int)Math.Round(size.Width * dpi.DpiScaleX),
        rect.Top + (int)Math.Round(size.Height * dpi.DpiScaleY));

    private bool TryGetWindowRect(out IntPtr hwnd, out PixelRect rect)
    {
        hwnd = _source?.Handle ?? IntPtr.Zero;
        rect = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var native))
            return false;

        rect = native.ToPixelRect();
        return true;
    }

    /// <summary>창 사각형과 가장 많이 겹치는 모니터의 작업 영역. 겹치는 모니터가 없으면 가장 가까운 모니터.</summary>
    internal static bool TryGetWorkArea(PixelRect rect, out PixelRect area)
    {
        area = default;
        var native = NativeRect.From(rect);
        var monitor = MonitorFromRect(ref native, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return false;

        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return false;

        area = info.rcWork.ToPixelRect();
        return true;
    }

    private static void MoveTo(IntPtr hwnd, PixelRect current, PixelRect target)
    {
        if (target.Left == current.Left && target.Top == current.Top)
            return;

        SetWindowPos(hwnd, IntPtr.Zero, target.Left, target.Top, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly PixelRect ToPixelRect() => new(Left, Top, Right, Bottom);

        public static NativeRect From(PixelRect r) =>
            new() { Left = r.Left, Top = r.Top, Right = r.Right, Bottom = r.Bottom };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref NativeRect rect, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
