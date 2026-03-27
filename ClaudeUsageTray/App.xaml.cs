using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using H.NotifyIcon;

namespace ClaudeUsageTray;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;
    private MainViewModel? _vm;
    private UsagePopup? _popup;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var credService = new CredentialService();
        var apiService = new UsageApiService(credService);
        var sessionMonitor = new SessionMonitor();

        _vm = new MainViewModel(apiService, sessionMonitor);

        _popup = new UsagePopup(_vm);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Claude Usage",
            Icon = DrawTrayIcon(0)
        };
        _trayIcon.TrayLeftMouseDown += OnTrayIconClick;
        _trayIcon.TrayRightMouseDown += OnTrayIconRightClick;

        // Update icon when usage changes
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ShortUsagePercent))
            {
                Dispatcher.Invoke(() =>
                {
                    _trayIcon.Icon?.Dispose();
                    _trayIcon.Icon = DrawTrayIcon(_vm.ShortUsagePercent);
                    _trayIcon.ToolTipText = $"Claude Usage · {_vm.ShortUsagePercent:P0} (5h)";
                });
            }
        };

        await _vm.StartAsync();
    }

    private void OnTrayIconClick(object? sender, RoutedEventArgs e)
    {
        if (_popup == null) return;

        if (_popup.IsVisible)
        {
            _popup.Hide();
        }
        else
        {
            _popup.ShowNearTray();
        }
    }

    private void OnTrayIconRightClick(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        var refreshItem = new MenuItem { Header = "Refresh" };
        refreshItem.Click += async (_, _) => await _vm!.RefreshAsync();
        menu.Items.Add(refreshItem);

        menu.Items.Add(new Separator());

        var quitItem = new MenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Shutdown();
        menu.Items.Add(quitItem);

        menu.IsOpen = true;
    }

    // Draw a 16x16 system tray icon with usage bars
    private static Icon DrawTrayIcon(double usagePercent)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        // Background pill
        using var bgBrush = new SolidBrush(Color.FromArgb(40, 139, 92, 246));
        g.FillRectangle(bgBrush, 1, 1, 14, 14);

        // Usage fill
        var fillColor = usagePercent < 0.6
            ? Color.FromArgb(139, 92, 246)      // Purple
            : usagePercent < 0.85
            ? Color.FromArgb(245, 158, 11)       // Amber
            : Color.FromArgb(239, 68, 68);       // Red

        var fillHeight = (int)(14 * usagePercent);
        if (fillHeight > 0)
        {
            using var fillBrush = new SolidBrush(fillColor);
            g.FillRectangle(fillBrush, 1, 15 - fillHeight, 14, fillHeight);
        }

        // Border
        using var borderPen = new Pen(Color.FromArgb(139, 92, 246), 1);
        g.DrawRectangle(borderPen, 1, 1, 13, 13);

        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
