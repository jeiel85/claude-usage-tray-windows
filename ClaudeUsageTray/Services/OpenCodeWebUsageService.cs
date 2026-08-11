using System.Globalization;
using System.Diagnostics;
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
    internal static readonly TimeSpan CachedFallbackMaxAge = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan SignedOutRetryDelay = TimeSpan.FromMinutes(30);
    /// <summary>확인 실패의 재시도 간격 단계 — 1·2·4·8·16분.</summary>
    private const int MaxUnavailableBackoffSteps = 5;
    private static readonly Uri AuthUri = new("https://opencode.ai/auth");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataRoot;
    private readonly string _workspaceRoutePath;
    private Window? _window;
    private WebView2? _webView;
    private bool _allowClose;
    private TaskCompletionSource<OpenCodeWebReadResult>? _pendingNavigation;
    private OpenCodeWebUsage? _cachedUsage;
    private DateTimeOffset _cacheExpiresAt;
    private DateTimeOffset _retryAfter;
    private OpenCodeWebSessionState _lastFailureState = OpenCodeWebSessionState.Unknown;
    private int _consecutiveUnavailable;
    private Uri? _workspaceUri;

    public string? LastError { get; private set; }
    internal Uri NavigationStartUri => _workspaceUri ?? AuthUri;

    /// <summary>
    /// 이 PC 에서 로그인에 성공해 작업공간 주소까지 저장해 둔 적이 있는가.
    /// 주소 파일은 공식 값을 실제로 읽어낸 뒤에만 쓰이므로 "여기서 한 번은 됐다" 의 증거가 된다.
    /// </summary>
    public bool HasSavedSession => _workspaceUri != null;

    public OpenCodeWebUsageService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageTray", "OpenCodeWebSession"))
    {
    }

    internal OpenCodeWebUsageService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _workspaceRoutePath = Path.Combine(dataRoot, "workspace-route.txt");
        _workspaceUri = ReadWorkspaceRoute(_workspaceRoutePath);
    }

    public async Task<OpenCodeWebReadResult> TryGetUsageAsync(bool interactive = false)
    {
        var now = DateTimeOffset.Now;
        if (!interactive && _cachedUsage != null && now < _cacheExpiresAt)
            return OpenCodeWebReadResult.Success(_cachedUsage);
        if (!interactive && now < _retryAfter)
            return WaitingForRetry(now);

        await _gate.WaitAsync();
        try
        {
            now = DateTimeOffset.Now;
            if (!interactive && _cachedUsage != null && now < _cacheExpiresAt)
                return OpenCodeWebReadResult.Success(_cachedUsage);
            if (!interactive && now < _retryAfter)
                return WaitingForRetry(now);

            LastError = null;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return OpenCodeWebReadResult.NotAttempted;

            var result = await dispatcher.InvokeAsync(() => NavigateAsync(interactive)).Task.Unwrap();
            if (result.Usage is { } usage)
            {
                _cachedUsage = usage;
                _cacheExpiresAt = DateTimeOffset.Now.AddMinutes(5);
                _retryAfter = default;
                _lastFailureState = OpenCodeWebSessionState.Unknown;
                _consecutiveUnavailable = 0;
                return result;
            }
            return interactive ? result : RegisterFailure(result.State, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return interactive
                ? OpenCodeWebReadResult.Unavailable
                : RegisterFailure(OpenCodeWebSessionState.Unavailable, DateTimeOffset.Now);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 실패를 기록하고 다음 확인 시각을 잡는다. 로그인이 풀린 경우엔 사용자가 로그인하기 전까지
    /// 결과가 달라지지 않으므로 길게 쉬고, 확인 자체를 못 한 경우엔 짧게 쉬었다가 다시 본다 —
    /// 부팅 직후 네트워크가 늦게 올라온 것뿐인데 30분 동안 로그인 버튼을 띄우고 있지 않도록.
    /// </summary>
    private OpenCodeWebReadResult RegisterFailure(OpenCodeWebSessionState state, DateTimeOffset now)
    {
        if (state is OpenCodeWebSessionState.Unknown or OpenCodeWebSessionState.Authenticated)
            state = OpenCodeWebSessionState.Unavailable;

        _lastFailureState = state;
        _consecutiveUnavailable = state == OpenCodeWebSessionState.Unavailable
            ? Math.Min(_consecutiveUnavailable + 1, MaxUnavailableBackoffSteps)
            : 0;
        _retryAfter = now.Add(RetryDelayFor(state, _consecutiveUnavailable));
        return WaitingForRetry(now);
    }

    /// <summary>재시도 대기 중 — 아직 쓸 수 있는 직전 값이 있으면 그걸 쓰고, 없으면 마지막 판정을 유지한다.</summary>
    private OpenCodeWebReadResult WaitingForRetry(DateTimeOffset now) =>
        GetCachedFallback(now) is { } usage
            ? OpenCodeWebReadResult.Success(usage)
            : new OpenCodeWebReadResult(null, _lastFailureState);

    internal static TimeSpan RetryDelayFor(OpenCodeWebSessionState state, int consecutiveUnavailable) =>
        state == OpenCodeWebSessionState.Unavailable
            ? TimeSpan.FromMinutes(1 << Math.Clamp(consecutiveUnavailable - 1, 0, MaxUnavailableBackoffSteps - 1))
            : SignedOutRetryDelay;

    private OpenCodeWebUsage? GetCachedFallback(DateTimeOffset now) =>
        IsCachedFallbackUsable(_cachedUsage, now) ? _cachedUsage : null;

    internal static bool IsCachedFallbackUsable(OpenCodeWebUsage? usage, DateTimeOffset now) =>
        usage?.ObservedAtUtc is { } observedAt &&
        observedAt <= now.AddMinutes(5) &&
        now - observedAt <= CachedFallbackMaxAge &&
        usage.Rolling.ResetAt > now;

    private async Task<OpenCodeWebReadResult> NavigateAsync(bool interactive)
    {
        await EnsureWebViewAsync(interactive);
        if (_window == null || _webView?.CoreWebView2 == null) return OpenCodeWebReadResult.Unavailable;

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
        var completion = new TaskCompletionSource<OpenCodeWebReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingNavigation = completion;
        cts.Token.Register(() =>
        {
            // 로그인 창을 열어둔 동안의 시간 초과는 "사용자가 안 끝냈다" 이므로 종전 문구를 남긴다.
            if (!interactive) LastError ??= Loc.OpenCodeWebNavigationTimedOut;
            completion.TrySetResult(OpenCodeWebReadResult.Unavailable);
        });

        async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (_webView?.CoreWebView2 == null) return;
            if (!args.IsSuccess)
            {
                // 부팅 직후처럼 네트워크가 아직 준비되지 않으면 탐색 자체가 실패한다.
                // 이걸 로그아웃과 같이 다루면 멀쩡한 세션에 로그인 버튼을 들이밀게 된다.
                LastError = Loc.OpenCodeWebNavigationFailed(args.WebErrorStatus.ToString());
                if (!interactive) completion.TrySetResult(OpenCodeWebReadResult.Unavailable);
                return;
            }

            var source = _webView.Source;
            if (source == null) return;
            if (source.Host.Equals("auth.opencode.ai", StringComparison.OrdinalIgnoreCase))
            {
                // 서버가 로그인 페이지로 되돌린 것 — 세션이 실제로 만료된 유일한 신호다.
                if (!interactive) completion.TrySetResult(OpenCodeWebReadResult.SignedOut);
                return;
            }
            if (!source.Host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)) return;

            var workspaceGoUri = NormalizeWorkspaceGoUri(source);
            if (workspaceGoUri == null)
            {
                // opencode.ai 안이지만 작업공간 페이지가 아니다. 로그인 여부를 단정할 수 없으므로
                // 로그인 버튼을 띄우지 않고 확인 실패로만 남긴다.
                if (!interactive)
                {
                    LastError = Loc.OpenCodeWebQuotaNotFound(source.AbsolutePath);
                    completion.TrySetResult(OpenCodeWebReadResult.Unavailable);
                }
                return;
            }
            if (!source.AbsolutePath.TrimEnd('/').EndsWith("/go", StringComparison.OrdinalIgnoreCase))
            {
                _webView.CoreWebView2.Navigate(workspaceGoUri.AbsoluteUri);
                return;
            }

            try
            {
                var encodedHtml = await _webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
                var html = JsonSerializer.Deserialize<string>(encodedHtml);
                var usage = html == null ? null : ParseUsage(html, DateTimeOffset.Now);
                if (usage == null)
                {
                    LastError = Loc.OpenCodeWebQuotaNotFound(source.AbsolutePath);
                    completion.TrySetResult(OpenCodeWebReadResult.Unavailable);
                    return;
                }

                _workspaceUri = workspaceGoUri;
                WriteWorkspaceRoute(_workspaceRoutePath, workspaceGoUri);
                completion.TrySetResult(OpenCodeWebReadResult.Success(usage));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                completion.TrySetResult(OpenCodeWebReadResult.Unavailable);
            }
        }

        _webView.NavigationCompleted += OnNavigationCompleted;
        // Source dependency property에 현재 주소와 같은 값을 다시 넣으면 WPF가 변경으로 보지 않아
        // 탐색 이벤트가 발생하지 않는다. 캐시 만료 후 자동 조회가 20초 타임아웃으로 끝나며
        // 로그인 해제로 오판하던 원인이므로 CoreWebView2에 매번 명시적으로 탐색을 요청한다.
        _webView.CoreWebView2.Navigate(NavigationStartUri.AbsoluteUri);
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

        Directory.CreateDirectory(_dataRoot);

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
            _pendingNavigation?.TrySetResult(OpenCodeWebReadResult.Unavailable);
        };

        // WPF WebView2 needs a presentation source for initialization. The off-screen window is
        // shown only long enough to create the control, then hidden until the user chooses login.
        _window.Show();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _dataRoot);
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

        return new OpenCodeWebUsage
        {
            ObservedAtUtc = observedAt.ToUniversalTime(),
            Rolling = rolling,
            Weekly = weekly,
            Monthly = monthly,
        };
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

    internal static Uri? NormalizeWorkspaceGoUri(Uri? uri)
    {
        if (uri == null
            || !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort)
            return null;

        var match = Regex.Match(uri.AbsolutePath,
            @"^/workspace/(?<id>[A-Za-z0-9_-]{3,128})(?:/go)?/?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        return new Uri($"https://opencode.ai/workspace/{match.Groups["id"].Value}/go");
    }

    internal static Uri? ReadWorkspaceRoute(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Uri.TryCreate(reader.ReadToEnd().Trim(), UriKind.Absolute, out var uri)
                ? NormalizeWorkspaceGoUri(uri)
                : null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"OpenCode workspace route could not be read: {ex.Message}");
            return null;
        }
    }

    internal static bool WriteWorkspaceRoute(string path, Uri uri)
    {
        var normalized = NormalizeWorkspaceGoUri(uri);
        if (normalized == null) return false;

        string? tempPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream))
                writer.Write(normalized.AbsoluteUri);
            File.Move(tempPath, path, true);
            tempPath = null;
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"OpenCode workspace route could not be saved: {ex.Message}");
            return false;
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) { Trace.TraceWarning($"OpenCode route temp file could not be removed: {ex.Message}"); }
            }
        }
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
