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
    private static Mutex? _mutex;
    private NotifyIcon? _trayIcon;
    private MainViewModel? _vm;
    private UsagePopup? _popup;
    private ToolStripMenuItem? _accountsMenu;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, "ClaudeUsageTray_SingleInstance_v1", out bool isNewInstance);
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show(
                "Claude Usage Tray가 이미 실행 중입니다.\n트레이 아이콘을 확인해 주세요.",
                "Claude Usage Tray",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            ShowCrashDialog(args.Exception);
            args.Handled = true;
            Shutdown(1);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                ShowCrashDialog(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ShowCrashDialog(args.Exception);
            args.SetObserved();
        };

        var settingsService = new SettingsService();
        var initialSettings = settingsService.Load();
        var activeProfile = initialSettings.Accounts.Count > 0
            ? initialSettings.Accounts[Math.Clamp(initialSettings.ActiveAccountIndex, 0, initialSettings.Accounts.Count - 1)]
            : null;
        var initialBaseDir = activeProfile is { ClaudeBaseDir: { Length: > 0 } d } ? d : null;

        var credService = new CredentialService(initialBaseDir);
        var apiService = new UsageApiService(credService);
        var sessionMonitor = new SessionMonitor(initialBaseDir);
        var notifier = new NotificationService();
        var updater = new UpdateService();
        var history = new HistoryService(initialBaseDir);

        _vm = new MainViewModel(apiService, credService, sessionMonitor, notifier, settingsService, updater, history);
        _popup = new UsagePopup(_vm);

        _trayIcon = new NotifyIcon
        {
            Text = "Claude Usage",
            Icon = DrawTrayIcon(0),
            Visible = true
        };

        _trayIcon.MouseClick += OnTrayClick;

        var contextMenu = new ContextMenuStrip();

        // Status summary items (read-only, non-clickable)
        var status5hItem = new ToolStripMenuItem("···") { Enabled = false };
        var status7dItem = new ToolStripMenuItem("···") { Enabled = false };
        contextMenu.Items.Add(status5hItem);
        contextMenu.Items.Add(status7dItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        var refreshItem = new ToolStripMenuItem("Refresh");
        refreshItem.Click += async (_, _) => await _vm.RefreshAsync();
        contextMenu.Items.Add(refreshItem);

        // 계정 서브메뉴 (계정이 2개 이상일 때만 표시)
        _accountsMenu = new ToolStripMenuItem("Account");
        contextMenu.Items.Add(_accountsMenu);
        contextMenu.Items.Add(new ToolStripSeparator());

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => Shutdown();
        contextMenu.Items.Add(quitItem);
        _trayIcon.ContextMenuStrip = contextMenu;

        // 메뉴 열릴 때 계정 목록 갱신
        contextMenu.Opening += (_, _) => RebuildAccountsMenu();

        // Update tray context menu status labels on data change
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ShortUsagePercent)
                                  or nameof(MainViewModel.LongUsagePercent)
                                  or nameof(MainViewModel.ShortResetLabel)
                                  or nameof(MainViewModel.LongResetLabel)
                                  or nameof(MainViewModel.HasError)
                                  or nameof(MainViewModel.IsLoading))
            {
                Dispatcher.Invoke(() =>
                {
                    if (_vm.IsLoading && _vm.ShortUsagePercent == 0)
                    {
                        status5hItem.Text = "5h: Loading...";
                        status7dItem.Text = "7d: Loading...";
                    }
                    else if (_vm.HasError)
                    {
                        status5hItem.Text = "5h: Unavailable";
                        status7dItem.Text = "7d: Unavailable";
                    }
                    else
                    {
                        var reset5h = _vm.ShortResetLabel.Replace(" · ", "  ");
                        var reset7d = _vm.LongResetLabel.Replace(" · ", "  ");
                        status5hItem.Text = $"5h: {_vm.ShortUsagePercent:P0}{reset5h}";
                        status7dItem.Text = $"7d: {_vm.LongUsagePercent:P0}{reset7d}";
                    }
                });
            }
        };

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

    private void RebuildAccountsMenu()
    {
        if (_accountsMenu is null || _vm is null) return;
        _accountsMenu.DropDownItems.Clear();

        var accounts = _vm.Accounts;
        _accountsMenu.Visible = accounts.Count >= 2;
        if (accounts.Count < 2) return;

        for (int i = 0; i < accounts.Count; i++)
        {
            var idx = i;
            var name = accounts[i].Name;
            var isActive = idx == _vm.ActiveAccountIndex;
            var item = new ToolStripMenuItem(isActive ? $"✓ {name}" : $"   {name}");
            item.Enabled = !isActive;
            item.Click += async (_, _) => await _vm.SwitchAccountAsync(idx);
            _accountsMenu.DropDownItems.Add(item);
        }
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

    private static void ShowCrashDialog(Exception ex)
    {
        var msg = $"Claude Usage Tray에서 예기치 않은 오류가 발생했습니다.\n\n" +
                  $"{ex.GetType().Name}: {ex.Message}\n\n" +
                  $"GitHub Issues에 아래 내용을 첨부해 신고해 주세요:\n{ex}";
        System.Windows.MessageBox.Show(msg, "Claude Usage Tray — 오류",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
