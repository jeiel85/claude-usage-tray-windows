using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using ClaudeUsageTray.Views;
using Application = System.Windows.Application;

namespace ClaudeUsageTray;

public partial class App : Application
{
    private NotifyIcon? _trayIcon;
    private MainViewModel? _vm;
    private UsagePopup? _popup;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var credService = new CredentialService();
        var apiService = new UsageApiService(credService);
        var sessionMonitor = new SessionMonitor();
        var notifier = new NotificationService();
        var settingsService = new SettingsService();
        var updater = new UpdateService();
        var history = new HistoryService();

        _vm = new MainViewModel(apiService, sessionMonitor, notifier, settingsService, updater, history);
        _popup = new UsagePopup(_vm);

        _trayIcon = new NotifyIcon
        {
            Text = "Claude Usage",
            Icon = DrawTrayIcon(0),
            Visible = true
        };

        _trayIcon.MouseClick += OnTrayClick;

        var contextMenu = new ContextMenuStrip();
        var refreshItem = new ToolStripMenuItem("Refresh");
        refreshItem.Click += async (_, _) => await _vm.RefreshAsync();
        contextMenu.Items.Add(refreshItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => Shutdown();
        contextMenu.Items.Add(quitItem);
        _trayIcon.ContextMenuStrip = contextMenu;

        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ShortUsagePercent) or nameof(MainViewModel.HasError))
            {
                Dispatcher.Invoke(() =>
                {
                    var oldIcon = _trayIcon.Icon;
                    if (_vm.HasError)
                    {
                        _trayIcon.Icon = DrawTrayIcon(-1);
                        _trayIcon.Text = "Claude Usage · ? (조회 실패)";
                    }
                    else
                    {
                        _trayIcon.Icon = DrawTrayIcon(_vm.ShortUsagePercent);
                        _trayIcon.Text = $"Claude Usage · {_vm.ShortUsagePercent:P0} (5h)";
                    }
                    oldIcon?.Dispose();
                });
            }
        };

        await _vm.StartAsync();
    }

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;

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

    // usagePercent = -1 means unknown/error state → shows "?"
    private static Icon DrawTrayIcon(double usagePercent)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        if (usagePercent < 0)
        {
            // Error state: gray background with "?" text
            using var bgBrush = new SolidBrush(Color.FromArgb(60, 60, 70));
            g.FillRectangle(bgBrush, 1, 1, 14, 14);
            using var borderPen = new Pen(Color.FromArgb(100, 100, 120), 1);
            g.DrawRectangle(borderPen, 1, 1, 13, 13);
            using var font = new Font(new FontFamily("Arial"), 8f, System.Drawing.FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.FromArgb(180, 180, 200));
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("?", font, textBrush, new RectangleF(1, 1, 14, 14), sf);
        }
        else
        {
            using var bgBrush = new SolidBrush(Color.FromArgb(40, 139, 92, 246));
            g.FillRectangle(bgBrush, 1, 1, 14, 14);

            var fillColor = usagePercent < 0.6
                ? Color.FromArgb(139, 92, 246)
                : usagePercent < 0.85
                ? Color.FromArgb(245, 158, 11)
                : Color.FromArgb(239, 68, 68);

            var fillHeight = (int)(14 * usagePercent);
            if (fillHeight > 0)
            {
                using var fillBrush = new SolidBrush(fillColor);
                g.FillRectangle(fillBrush, 1, 15 - fillHeight, 14, fillHeight);
            }

            using var borderPen = new Pen(Color.FromArgb(139, 92, 246), 1);
            g.DrawRectangle(borderPen, 1, 1, 13, 13);
        }

        var hIcon = bmp.GetHicon();
        return Icon.FromHandle(hIcon);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnExit(e);
    }
}
