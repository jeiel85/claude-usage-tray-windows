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

    // Menu item references for status updates
    private ToolStripMenuItem? _claudeStatusItem;
    private ToolStripMenuItem? _codexStatusItem;
    private ToolStripMenuItem? _geminiStatusItem;
    private ToolStripMenuItem? _openCodeStatusItem;
    private ToolStripMenuItem? _nextRefreshItem;

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
        var credService = new CredentialService();
        var apiService = new UsageApiService(credService);
        var sessionMonitor = new SessionMonitor();
        var codexMonitor = new CodexUsageMonitor();
        var geminiCliMonitor = new GeminiCliUsageMonitor();
        var openCodeMonitor = new OpenCodeUsageMonitor();
        var notifier = new NotificationService(() => _trayIcon);
        var updater = new UpdateService();
        var history = new HistoryService();

        _vm = new MainViewModel(apiService, credService, sessionMonitor, codexMonitor, geminiCliMonitor,
            openCodeMonitor, notifier, settingsService, updater, history);
        _popup = new UsagePopup(_vm);

        _trayIcon = new NotifyIcon
        {
            Text = Loc.AgentUsageTitle,
            Icon = DrawTrayIcon(0),
            Visible = true
        };

        _trayIcon.MouseClick += OnTrayClick;

        var contextMenu = new ContextMenuStrip();

        // Provider status items (read-only)
        _claudeStatusItem    = new ToolStripMenuItem("Claude: ---")   { Enabled = false };
        _codexStatusItem     = new ToolStripMenuItem("Codex: ---")    { Enabled = false };
        _geminiStatusItem    = new ToolStripMenuItem("Gemini: ---")   { Enabled = false };
        _openCodeStatusItem  = new ToolStripMenuItem("OpenCode: ---") { Enabled = false };
        _nextRefreshItem     = new ToolStripMenuItem("Next refresh: --") { Enabled = false };

        contextMenu.Items.Add(_claudeStatusItem);
        contextMenu.Items.Add(_codexStatusItem);
        contextMenu.Items.Add(_geminiStatusItem);
        contextMenu.Items.Add(_openCodeStatusItem);
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_nextRefreshItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        var refreshItem = new ToolStripMenuItem("Refresh");
        refreshItem.Click += async (_, _) => await _vm.RefreshAsync();
        contextMenu.Items.Add(refreshItem);
        contextMenu.Items.Add(new ToolStripSeparator());

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => Shutdown();
        contextMenu.Items.Add(quitItem);
        _trayIcon.ContextMenuStrip = contextMenu;

        // Subscribe to PropertyChanged for status menu updates
        _vm.PropertyChanged += OnVmStatusPropertyChanged;

        // Subscribe to PropertyChanged for tray icon updates
        _vm.PropertyChanged += OnVmIconPropertyChanged;

        await _vm.StartAsync();
    }

    private void OnVmStatusPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.ClaudeShortPercent)
                              or nameof(MainViewModel.ClaudeLongPercent)
                              or nameof(MainViewModel.CodexPercent)
                              or nameof(MainViewModel.GeminiRequestsLabel)
                              or nameof(MainViewModel.GeminiOutputTokensLabel)
                              or nameof(MainViewModel.OpenCodeRequestCountLabel)
                              or nameof(MainViewModel.OpenCodeInputLabel)
                              or nameof(MainViewModel.OpenCodeOutputLabel)
                              or nameof(MainViewModel.ClaudeHasError)
                              or nameof(MainViewModel.CodexHasError)
                              or nameof(MainViewModel.GeminiHasError)
                              or nameof(MainViewModel.OpenCodeHasError)
                              or nameof(MainViewModel.IsLoading)
                              or nameof(MainViewModel.NextRefreshLabel))
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm is null || _claudeStatusItem is null) return;

                if (_vm.IsLoading && _vm.ClaudeShortPercent == 0 && _vm.CodexPercent == 0)
                {
                    _claudeStatusItem.Text   = "Claude: Loading...";
                    _codexStatusItem!.Text   = "Codex: Loading...";
                    _geminiStatusItem!.Text  = "Gemini: Loading...";
                    _openCodeStatusItem!.Text = "OpenCode: Loading...";
                }
                else
                {
                    // Claude Status
                    if (_vm.ClaudeHasError) _claudeStatusItem.Text = $"Claude: {Loc.Unavailable}";
                    else _claudeStatusItem.Text = $"Claude: {_vm.ClaudeShortPercent:P0} (5h) / {_vm.ClaudeLongPercent:P0} (7d)";

                    // Codex Status
                    if (_vm.CodexHasError) _codexStatusItem!.Text = $"Codex: {Loc.Unavailable}";
                    else _codexStatusItem!.Text = $"Codex: {_vm.CodexPercent:P0}";

                    // Gemini Status (token-based, no %)
                    if (_vm.GeminiHasError) _geminiStatusItem!.Text = $"Gemini: {Loc.Unavailable}";
                    else if (_vm.GeminiRequestsLabel == "—" || string.IsNullOrEmpty(_vm.GeminiRequestsLabel))
                        _geminiStatusItem!.Text = "Gemini: —";
                    else _geminiStatusItem!.Text = $"Gemini: {_vm.GeminiRequestsLabel} · {_vm.GeminiOutputTokensLabel}";

                    // OpenCode Status (token-based)
                    if (_vm.OpenCodeHasError) _openCodeStatusItem!.Text = $"OpenCode: {Loc.Unavailable}";
                    else if (_vm.OpenCodeRequestCountLabel == "—" || string.IsNullOrEmpty(_vm.OpenCodeRequestCountLabel))
                        _openCodeStatusItem!.Text = "OpenCode: —";
                    else _openCodeStatusItem!.Text = $"OpenCode: {_vm.OpenCodeRequestCountLabel} · in {_vm.OpenCodeInputLabel} · out {_vm.OpenCodeOutputLabel}";
                }

                _nextRefreshItem!.Text = $"Next: {_vm.NextRefreshLabel}";
            });
        }
    }

    private void OnVmIconPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(MainViewModel.TrayUsagePercent)
                              or nameof(MainViewModel.ClaudeHasError)
                              or nameof(MainViewModel.CodexHasError)
                              or nameof(MainViewModel.GeminiHasError)
                              or nameof(MainViewModel.OpenCodeHasError)
                              or nameof(MainViewModel.HasError)
                              or nameof(MainViewModel.LblAppTitle))
        {
            Dispatcher.Invoke(() =>
            {
                if (_vm is null || _trayIcon is null) return;

                // Tray gauge reflects the selected or auto-prioritized provider
                var oldIcon = _trayIcon.Icon;
                
                // Determine if the current active provider has an error
                bool currentHasError = _vm.TrayDisplayMode switch
                {
                    UsageProviderKind.Claude => _vm.ClaudeHasError,
                    UsageProviderKind.Codex => _vm.CodexHasError,
                    UsageProviderKind.GeminiCli => _vm.GeminiHasError,
                    UsageProviderKind.OpenCode => _vm.OpenCodeHasError,
                    _ => _vm.ClaudeHasError && _vm.CodexHasError && _vm.GeminiHasError && _vm.OpenCodeHasError // Auto mode: only error if all fail
                };

                _trayIcon.Icon = currentHasError ? DrawTrayIcon(-1) : DrawTrayIcon(_vm.TrayUsagePercent);
                oldIcon?.Dispose();

                // Multi-provider summary in tooltip (token-based for Gemini/OpenCode)
                string claudeInfo    = _vm.ClaudeHasError    ? "—" : $"{_vm.ClaudeShortPercent:P0}";
                string codexInfo     = _vm.CodexHasError     ? "—" : $"{_vm.CodexPercent:P0}";
                string geminiInfo    = _vm.GeminiHasError    ? "—" : (_vm.GeminiOutputTokensLabel is "—" or "" ? "-" : _vm.GeminiOutputTokensLabel);
                string openCodeInfo  = _vm.OpenCodeHasError  ? "—" : (_vm.OpenCodeOutputLabel    is "—" or "" ? "-" : _vm.OpenCodeOutputLabel);

                string tooltip = $"{Loc.AgentUsageTitle}\nClaude {claudeInfo} · Codex {codexInfo} · Gemini {geminiInfo} · OC {openCodeInfo}";
                _trayIcon.Text = tooltip.Length > 63 ? tooltip[..63] : tooltip;
            });
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
        using (var g = Graphics.FromImage(bmp))
        {
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
        }

        var hIcon = bmp.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        // GDI+ 리소스 보호: Bitmap은 using에서 Dispose되지만, GetHicon으로 만든 Icon은
        // 명시적으로 DestroyIcon이 호출되지 않으면 GDI 리소스가 leak됨
        // Icon.FromHandle은 내부적으로 HICON을 소유하므로, Icon.Dispose() 호출 시 자동 정리됨
        return icon;
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
        // Unsubscribe from PropertyChanged to prevent memory leaks
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnVmStatusPropertyChanged;
            _vm.PropertyChanged -= OnVmIconPropertyChanged;
        }

        _vm?.Dispose();
        _popup?.Dispose();

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
