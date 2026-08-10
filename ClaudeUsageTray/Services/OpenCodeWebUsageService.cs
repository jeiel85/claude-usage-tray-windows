using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTray.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Reads the real OpenCode Go quota from an app-private web session. The service never reads
/// Chrome/Edge profiles or copies browser cookies into application settings.
/// </summary>
public sealed class OpenCodeWebUsageService : IDisposable
{
    private static readonly Uri WorkspaceUri = new("https://opencode.ai/workspace");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Window? _window;
    private WebView2? _webView;
    private bool _allowClose;
    private TaskCompletionSource<OpenCodeWebUsage?>? _pendingNavigation;
    private OpenCodeWebUsage? _cachedUsage;
    private DateTimeOffset _cacheExpiresAt;
    private DateTimeOffset _retryAfter;

    public string? LastError { get; private set; }

    public async Task<OpenCodeWebUsage?> TryGetUsageAsync(bool interactive = false)
    {
        var now = DateTimeOffset.Now;
        if (!interactive && _cachedUsage != null && now < _cacheExpiresAt)
            return _cachedUsage;
        if (!interactive && now < _retryAfter)
            return null;

        await _gate.WaitAsync();
        try
        {
            now = DateTimeOffset.Now;
            if (!interactive && _cachedUsage != null && now < _cacheExpiresAt)
                return _cachedUsage;

            LastError = null;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return null;

            var usage = await dispatcher.InvokeAsync(() => NavigateAsync(interactive)).Task.Unwrap();
            if (usage != null)
            {
                _cachedUsage = usage;
                _cacheExpiresAt = DateTimeOffset.Now.AddMinutes(5);
                _retryAfter = default;
            }
            else if (!interactive)
            {
                _retryAfter = DateTimeOffset.Now.AddMinutes(30);
            }
            return usage;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _retryAfter = DateTimeOffset.Now.AddMinutes(30);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OpenCodeWebUsage?> NavigateAsync(bool interactive)
    {
        await EnsureWebViewAsync(interactive);
        if (_window == null || _webView?.CoreWebView2 == null) return null;

        if (interactive)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
            var dpi = VisualTreeHelper.GetDpi(_window);
            var workingArea = new Rect(
                screen.WorkingArea.Left / dpi.DpiScaleX,
                screen.WorkingArea.Top / dpi.DpiScaleY,
                screen.WorkingArea.Width / dpi.DpiScaleX,
                screen.WorkingArea.Height / dpi.DpiScaleY);
            var bounds = CenterWithinWorkArea(workingArea, new System.Windows.Size(_window.Width, _window.Height));

            _window.WindowState = WindowState.Normal;
            _window.Width = bounds.Width;
            _window.Height = bounds.Height;
            _window.Left = bounds.Left;
            _window.Top = bounds.Top;
            _window.Opacity = 1;
            _window.ShowInTaskbar = true;
            _window.Topmost = true;
            _window.Show();
            _window.Activate();
            _ = _window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (_window != null) _window.Topmost = false;
            });
        }

        var timeout = interactive ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(20);
        using var cts = new CancellationTokenSource(timeout);
        var completion = new TaskCompletionSource<OpenCodeWebUsage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingNavigation = completion;
        cts.Token.Register(() => completion.TrySetResult(null));

        async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess || _webView?.CoreWebView2 == null) return;

            var source = _webView.Source;
            if (source == null) return;
            if (source.Host.Equals("auth.opencode.ai", StringComparison.OrdinalIgnoreCase))
            {
                if (!interactive) completion.TrySetResult(null);
                return;
            }
            if (!source.Host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)) return;

            var workspaceMatch = Regex.Match(source.AbsolutePath, @"^/workspace/(?<id>[^/]+)");
            if (!workspaceMatch.Success) return;
            if (!source.AbsolutePath.EndsWith("/go", StringComparison.OrdinalIgnoreCase))
            {
                _webView.Source = new Uri($"https://opencode.ai/workspace/{workspaceMatch.Groups["id"].Value}/go");
                return;
            }

            try
            {
                var encodedHtml = await _webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                var html = JsonSerializer.Deserialize<string>(encodedHtml);
                completion.TrySetResult(html == null ? null : ParseUsage(html, DateTimeOffset.Now));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                completion.TrySetResult(null);
            }
        }

        _webView.NavigationCompleted += OnNavigationCompleted;
        _webView.Source = WorkspaceUri;
        try
        {
            return await completion.Task;
        }
        finally
        {
            _webView.NavigationCompleted -= OnNavigationCompleted;
            _pendingNavigation = null;
            if (_window.IsVisible) _window.Hide();
        }
    }

    private async Task EnsureWebViewAsync(bool interactive)
    {
        if (_webView?.CoreWebView2 != null) return;

        var dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageTray", "OpenCodeWebSession");
        Directory.CreateDirectory(dataRoot);

        _webView = new WebView2();
        _window = new Window
        {
            Title = Loc.OpenCodeWebLoginTitle,
            Width = 920,
            Height = 720,
            MinWidth = 640,
            MinHeight = 520,
            Content = _webView,
            ShowInTaskbar = interactive,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            Opacity = 0
        };
        _window.Closing += (_, args) =>
        {
            if (_allowClose) return;
            args.Cancel = true;
            _window.Hide();
            _pendingNavigation?.TrySetResult(null);
        };

        // WPF WebView2 needs a presentation source for initialization. The off-screen window is
        // shown only long enough to create the control, then hidden until the user chooses login.
        _window.Show();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataRoot);
        await _webView.EnsureCoreWebView2Async(environment);
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        if (!interactive) _window.Hide();
    }

    internal static OpenCodeWebUsage? ParseUsage(string html, DateTimeOffset observedAt)
    {
        var rolling = ParseWindow(html, "rollingUsage", observedAt);
        var weekly = ParseWindow(html, "weeklyUsage", observedAt);
        var monthly = ParseWindow(html, "monthlyUsage", observedAt);
        if (rolling == null || weekly == null || monthly == null) return null;

        return new OpenCodeWebUsage { Rolling = rolling, Weekly = weekly, Monthly = monthly };
    }

    internal static Rect CenterWithinWorkArea(Rect workArea, System.Windows.Size requestedSize)
    {
        var width = Math.Min(requestedSize.Width, workArea.Width);
        var height = Math.Min(requestedSize.Height, workArea.Height);
        return new Rect(
            workArea.Left + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Top + Math.Max(0, (workArea.Height - height) / 2),
            width,
            height);
    }

    private static OpenCodeQuotaWindow? ParseWindow(string html, string name, DateTimeOffset observedAt)
    {
        var block = Regex.Match(html,
            $@"{Regex.Escape(name)}:\$R\[\d+\]=\{{(?<body>[^}}]+)\}}",
            RegexOptions.CultureInvariant);
        if (!block.Success) return null;

        var body = block.Groups["body"].Value;
        var resetMatch = Regex.Match(body, @"resetInSec:(?<value>\d+)", RegexOptions.CultureInvariant);
        var percentMatch = Regex.Match(body, @"usagePercent:(?<value>\d+(?:\.\d+)?)", RegexOptions.CultureInvariant);
        if (!resetMatch.Success || !percentMatch.Success
            || !double.TryParse(resetMatch.Groups["value"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var resetSeconds)
            || !double.TryParse(percentMatch.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            || resetSeconds < 0 || percent is < 0 or > 100)
            return null;

        return new OpenCodeQuotaWindow
        {
            UsagePercent = percent / 100d,
            ResetAt = observedAt.AddSeconds(resetSeconds)
        };
    }

    public void Dispose()
    {
        _gate.Dispose();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.Invoke(() =>
        {
            _allowClose = true;
            _webView?.Dispose();
            _window?.Close();
            _webView = null;
            _window = null;
        });
    }
}
