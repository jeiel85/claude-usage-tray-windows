using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeUsageTray.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Claude Console(platform.claude.com)의 프로모션 크레딧을 앱 전용 웹 세션으로 읽는다(#354).
/// </summary>
/// <remarks>
/// Console API 는 Claude Code OAuth 토큰을 받지 않는다(403 <c>oauth_token_not_accepted</c>, 2026-11-27 실측).
/// 그래서 <see cref="OpenCodeWebUsageService"/> 와 같이 앱 전용 WebView2 에서 사용자가 직접 로그인하고, 그
/// platform.claude.com 문서 안에서 <c>fetch</c> 로 부른다 — 쿠키는 WebView2 데이터 폴더 밖으로 나가지 않고,
/// 브라우저 프로필은 읽지 않는다. 이 PC 에서 연결한 적이 없으면 WebView2 를 만들지 않는다.
/// </remarks>
public sealed class ClaudeConsoleCreditService : IDisposable
{
    internal static readonly Uri BillingUri = new("https://platform.claude.com/settings/billing");
    /// <summary>자동 조회가 여는 문서 — 스크립트 없는 JSON 이라 SPA 처럼 문서를 갈아치우지 않는다(비로그인이면 403 JSON).</summary>
    internal static readonly Uri OrganizationsUri = new("https://platform.claude.com/api/organizations");
    internal const string OrganizationsPath = "/api/organizations";
    /// <summary>Console 이 세션 없는 요청에 주는 오류 코드. 이것만 로그아웃으로 본다(Cloudflare 403 등과 구별).</summary>
    internal const string SessionInvalidCode = "account_session_invalid";
    private const string ApiCapability = "api";
    private const int MaxOrgsToProbe = 10;
    private const int MaxUnavailableBackoffSteps = 5;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SignedOutRetryDelay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SignInPollInterval = TimeSpan.FromSeconds(2);
    /// <summary>이메일 코드 로그인은 메일을 열고 오는 시간이 든다.</summary>
    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackgroundReadTimeout = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataRoot;
    private readonly string _orgIdPath;
    private readonly Dictionary<string, TaskCompletionSource<ConsoleResponse>> _pendingFetches = new();
    private Window? _window;
    private WebView2? _webView;
    private bool _allowClose;
    private volatile bool _disposed;
    private CancellationTokenSource? _activeRead;
    private ClaudeConsoleReadResult? _cached;
    private DateTimeOffset _cacheExpiresAt;
    private DateTimeOffset _retryAfter;
    private ClaudeConsoleReadResult _lastFailure = ClaudeConsoleReadResult.Unavailable;
    private int _consecutiveUnavailable;
    private string? _orgId;

    public string? LastError { get; private set; }

    /// <summary>이 PC 에서 크레딧을 실제로 읽어 조직을 저장해 둔 적이 있는가 — 없으면 자동 조회를 하지 않는다.</summary>
    public bool HasSavedSession => _orgId != null;

    public ClaudeConsoleCreditService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageTray", "ClaudeConsoleSession"))
    {
    }

    internal ClaudeConsoleCreditService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _orgIdPath = Path.Combine(dataRoot, "console-org.txt");
        _orgId = ReadOrgId(_orgIdPath);
    }

    public async Task<ClaudeConsoleReadResult> TryGetCreditsAsync(bool interactive = false)
    {
        if (!interactive && !HasSavedSession) return ClaudeConsoleReadResult.NotConnected;
        var now = DateTimeOffset.Now;
        if (!interactive && _cached != null && now < _cacheExpiresAt) return _cached;
        if (!interactive && now < _retryAfter) return _lastFailure;

        await _gate.WaitAsync();
        try
        {
            now = DateTimeOffset.Now;
            if (!interactive && _cached != null && now < _cacheExpiresAt) return _cached;
            if (!interactive && now < _retryAfter) return _lastFailure;

            LastError = null;
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return ClaudeConsoleReadResult.Unavailable;

            var result = await dispatcher.InvokeAsync(() => NavigateAsync(interactive)).Task.Unwrap();
            if (result.State == ClaudeConsoleSessionState.Ok)
            {
                _cached = result;
                _cacheExpiresAt = DateTimeOffset.Now.Add(CacheDuration);
                _retryAfter = default;
                _consecutiveUnavailable = 0;
                return result;
            }
            return interactive ? result : RegisterFailure(result, DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return interactive
                ? ClaudeConsoleReadResult.Unavailable
                : RegisterFailure(ClaudeConsoleReadResult.Unavailable, DateTimeOffset.Now);
        }
        finally
        {
            if (!_disposed) _gate.Release();
        }
    }

    /// <summary>
    /// 연결을 끊는다 — 저장한 조직과 앱 전용 세션 쿠키를 지운다. 브라우저의 Console 로그인에는 영향이 없다.
    /// </summary>
    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _orgId = null;
            _cached = null;
            _cacheExpiresAt = default;
            _retryAfter = default;
            _consecutiveUnavailable = 0;
            _lastFailure = ClaudeConsoleReadResult.Unavailable;
            try
            {
                if (File.Exists(_orgIdPath)) File.Delete(_orgIdPath);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Claude Console organization file could not be removed: {ex.Message}");
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            var cleared = dispatcher != null && await dispatcher.InvokeAsync(() =>
            {
                if (_webView?.CoreWebView2 is not { } core) return false;
                core.CookieManager.DeleteAllCookies();
                return true;
            });
            // 이번 실행에서 WebView2 를 만든 적이 없으면 폴더가 잠겨 있지 않다 — 세션 데이터를 통째로 지운다.
            if (!cleared) TryDeleteDirectory(_dataRoot);
        }
        finally
        {
            if (!_disposed) _gate.Release();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Claude Console session folder could not be removed: {ex.Message}");
        }
    }

    private ClaudeConsoleReadResult RegisterFailure(ClaudeConsoleReadResult result, DateTimeOffset now)
    {
        if (result.State is not (ClaudeConsoleSessionState.SignedOut or ClaudeConsoleSessionState.Unavailable))
            result = ClaudeConsoleReadResult.Unavailable;
        _consecutiveUnavailable = result.State == ClaudeConsoleSessionState.Unavailable
            ? Math.Min(_consecutiveUnavailable + 1, MaxUnavailableBackoffSteps)
            : 0;
        _retryAfter = now.Add(RetryDelayFor(result.State, _consecutiveUnavailable));
        // 세션은 멀쩡한데 잠깐 못 읽었으면, 아직 유효한 직전 값을 계속 보여 준다(크레딧은 천천히 바뀐다).
        if (result.State == ClaudeConsoleSessionState.Unavailable && _cached != null)
        {
            _lastFailure = _cached;
            return _cached;
        }
        _cached = null;
        _lastFailure = result;
        return result;
    }

    internal static TimeSpan RetryDelayFor(ClaudeConsoleSessionState state, int consecutiveUnavailable) =>
        state == ClaudeConsoleSessionState.Unavailable
            ? TimeSpan.FromMinutes(1 << Math.Clamp(consecutiveUnavailable - 1, 0, MaxUnavailableBackoffSteps - 1))
            : SignedOutRetryDelay;

    private async Task<ClaudeConsoleReadResult> NavigateAsync(bool interactive)
    {
        await EnsureWebViewAsync(interactive);
        if (_window == null || _webView?.CoreWebView2 == null) return ClaudeConsoleReadResult.Unavailable;

        if (interactive) ShowLoginWindow(_window);

        using var cts = new CancellationTokenSource(interactive ? SignInTimeout : BackgroundReadTimeout);
        _activeRead = cts;
        try
        {
            return interactive
                ? await WaitForSignInAsync(cts.Token)
                : await ReadOnceAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!interactive) LastError ??= Loc.ConsoleCreditsReadTimedOut;
            return ClaudeConsoleReadResult.Unavailable;
        }
        finally
        {
            _activeRead = null;
            foreach (var pending in _pendingFetches.Values) pending.TrySetResult(ConsoleResponse.NoResponse);
            _pendingFetches.Clear();
            if (_window is { IsVisible: true } window) window.Hide();
        }
    }

    private static void ShowLoginWindow(Window window)
    {
        var screen = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);
        var dpi = VisualTreeHelper.GetDpi(window);
        var workingArea = new Rect(
            screen.WorkingArea.Left / dpi.DpiScaleX,
            screen.WorkingArea.Top / dpi.DpiScaleY,
            screen.WorkingArea.Width / dpi.DpiScaleX,
            screen.WorkingArea.Height / dpi.DpiScaleY);
        var bounds = OpenCodeWebUsageService.CenterWithinWorkArea(
            workingArea, new System.Windows.Size(window.Width, window.Height));

        window.WindowState = WindowState.Normal;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Opacity = 1;
        window.ShowInTaskbar = true;
        window.Topmost = true;
        window.Show();
        window.Activate();
        _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => window.Topmost = false);
    }

    private async Task<ClaudeConsoleReadResult> ReadOnceAsync(CancellationToken cancellationToken)
    {
        if (!await OpenDocumentAsync(OrganizationsUri, cancellationToken)) return ClaudeConsoleReadResult.Unavailable;
        return Finish(await ReadCreditsAsync(path => FetchAsync(path, cancellationToken), _orgId, DateTimeOffset.Now));
    }

    /// <summary>
    /// 로그인 창 — Console 결제 화면을 보여 주고, 사용자가 로그인을 마칠 때까지 같은 문서 안에서 주기적으로 읽어 본다.
    /// Google 같은 로그인 공급자 페이지에 있는 동안은 묻지 않는다.
    /// </summary>
    private async Task<ClaudeConsoleReadResult> WaitForSignInAsync(CancellationToken cancellationToken)
    {
        _webView!.CoreWebView2.Navigate(BillingUri.AbsoluteUri);
        while (true)
        {
            await Task.Delay(SignInPollInterval, cancellationToken);
            if (!IsConsoleOrigin(_webView.CoreWebView2.Source)) continue;

            var outcome = await ReadCreditsAsync(path => FetchAsync(path, cancellationToken), _orgId, DateTimeOffset.Now);
            if (outcome.State == ClaudeConsoleSessionState.Ok) return Finish(outcome);
        }
    }

    private ClaudeConsoleReadResult Finish(CreditReadOutcome outcome)
    {
        if (outcome.State == ClaudeConsoleSessionState.Ok && outcome.OrgId is { } orgId && orgId != _orgId)
        {
            _orgId = orgId;
            WriteOrgId(_orgIdPath, orgId);
        }
        LastError = outcome.Error;
        return new ClaudeConsoleReadResult(outcome.State, outcome.Credits);
    }

    /// <summary>
    /// 저장한 조직의 크레딧을 먼저 묻고, 안 되면 조직 목록에서 <c>api</c> 기능이 있는 조직을 찾는다.
    /// 로그아웃 판정은 조직 목록이 <see cref="SessionInvalidCode"/> 로 거절할 때만 한다 — 특정 조직의 403 은
    /// 권한 문제(claude.ai 구독 조직은 403)일 수 있다.
    /// </summary>
    internal static async Task<CreditReadOutcome> ReadCreditsAsync(
        Func<string, Task<ConsoleResponse>> fetch, string? savedOrgId, DateTimeOffset now)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 프로모션 크레딧이 없는 조직(팀 API 조직 등)은 다른 조직을 다 본 뒤에만 쓴다(PR #355 리뷰) — 먼저 만났다고
        // 멈추면 그 조직이 저장돼, Max 크레딧이 있는 개인 조직을 영영 묻지 않는다.
        CreditReadOutcome? empty = null;
        if (savedOrgId != null)
        {
            var saved = await fetch(CreditsPath(savedOrgId));
            if (saved.Status == 200 && TryParseCredits(saved.Body, now, out var credits))
            {
                if (credits != null) return CreditReadOutcome.Ok(credits, savedOrgId);
                empty = CreditReadOutcome.Ok(null, savedOrgId);
            }
            if (saved.Status != 0 && saved.Status < 500) tried.Add(savedOrgId);
        }

        var orgs = await fetch(OrganizationsPath);
        if (IsSessionInvalid(orgs)) return CreditReadOutcome.SignedOut;
        if (orgs.Status != 200) return empty ?? CreditReadOutcome.Failed(Describe(orgs.Status));

        string? failure = null;
        foreach (var orgId in ParseApiOrganizationIds(orgs.Body))
        {
            if (!tried.Add(orgId)) continue;
            if (tried.Count > MaxOrgsToProbe) break;
            var response = await fetch(CreditsPath(orgId));
            if (response.Status == 200 && TryParseCredits(response.Body, now, out var credits))
            {
                if (credits != null) return CreditReadOutcome.Ok(credits, orgId);
                empty ??= CreditReadOutcome.Ok(null, orgId);
                continue;
            }
            failure ??= Describe(response.Status);
        }
        return empty ?? CreditReadOutcome.Failed(failure ?? Loc.ConsoleCreditsNoApiOrganization);
    }

    internal static string CreditsPath(string orgId) => $"/api/organizations/{orgId}/prepaid/credits";

    private static bool IsSessionInvalid(ConsoleResponse response) =>
        response.Status is 401 or 403 && response.Body.Contains(SessionInvalidCode, StringComparison.Ordinal);

    private static string Describe(int status) =>
        Loc.ConsoleCreditsReadFailed(status == 0 ? "no response" : $"HTTP {status}");

    /// <summary>조직 목록에서 API 를 쓰는 조직(<c>capabilities</c> 에 <c>api</c>)의 UUID — Console 크레딧은 거기에만 있다.</summary>
    internal static IReadOnlyList<string> ParseApiOrganizationIds(string? json)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return ids;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return ids;
            foreach (var org in document.RootElement.EnumerateArray())
            {
                if (org.ValueKind != JsonValueKind.Object
                    || !org.TryGetProperty("uuid", out var uuid) || uuid.ValueKind != JsonValueKind.String
                    || uuid.GetString() is not { } id || !IsValidOrgId(id)
                    || !org.TryGetProperty("capabilities", out var caps) || caps.ValueKind != JsonValueKind.Array)
                    continue;
                if (caps.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == ApiCapability)
                    && !ids.Contains(id, StringComparer.OrdinalIgnoreCase))
                    ids.Add(id);
            }
        }
        catch (JsonException)
        {
        }
        return ids;
    }

    /// <summary>
    /// <c>prepaid/credits</c> 의 <c>promo_tranches</c> 를 합친다 — 잔액·지급액은 만료되지 않은 같은 통화 항목의 합,
    /// 만료일은 잔액이 남은 항목 중 가장 이른 날. 프로모션 크레딧이 없으면 <paramref name="credits"/> 는 null(읽기는 성공).
    /// 구매한 크레딧(<c>tranches</c>)은 넣지 않는다 — Max 구독에 딸린 크레딧을 보려는 기능이다.
    /// </summary>
    internal static bool TryParseCredits(string? json, DateTimeOffset now, out ClaudeConsoleCredits? credits)
    {
        credits = null;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("promo_tranches", out var tranches)) return false;
            if (tranches.ValueKind == JsonValueKind.Null) return true;
            if (tranches.ValueKind != JsonValueKind.Array) return false;

            string? currency = null;
            int? exponent = null;
            long remaining = 0, granted = 0;
            DateTimeOffset? expiresAt = null;
            foreach (var tranche in tranches.EnumerateArray())
            {
                if (tranche.ValueKind != JsonValueKind.Object) continue;
                var expiry = ReadDate(tranche, "expires_at");
                if (expiry is { } e && e <= now) continue;
                var trancheCurrency = ReadString(tranche, "currency")?.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(trancheCurrency)) continue;
                if (currency != null && currency != trancheCurrency) continue;
                if (ReadLong(tranche, "remaining_amount_minor_units") is not { } left || left < 0
                    || ReadLong(tranche, "granted_amount_minor_units") is not { } given || given <= 0)
                    continue;

                currency = trancheCurrency;
                exponent ??= ReadExponent(tranche);
                remaining += left;
                granted += given;
                if (left > 0 && expiry is { } until && (expiresAt == null || until < expiresAt)) expiresAt = until;
            }

            if (currency != null)
                credits = new ClaudeConsoleCredits(remaining, granted, exponent ?? 2, currency, expiresAt);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary><c>remaining.credits.exponent</c> 또는 <c>granted.credits.exponent</c>. 없으면 null(호출부가 2).</summary>
    private static int? ReadExponent(JsonElement tranche)
    {
        foreach (var name in new[] { "remaining", "granted" })
        {
            if (tranche.TryGetProperty(name, out var amount) && amount.ValueKind == JsonValueKind.Object
                && amount.TryGetProperty("credits", out var creditsAmount) && creditsAmount.ValueKind == JsonValueKind.Object
                && creditsAmount.TryGetProperty("exponent", out var exp) && exp.TryGetInt32(out var value)
                && value is >= 0 and <= 6)
                return value;
        }
        return null;
    }

    private static long? ReadLong(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)
            ? n
            : null;

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? ReadDate(JsonElement parent, string name) =>
        ReadString(parent, name) is { } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime()
            : null;

    /// <summary>숨은 WebView2 에 문서를 열고 탐색 완료를 기다린다. 응답 코드조차 없을 때만 실패(네트워크 미준비 등).</summary>
    private async Task<bool> OpenDocumentAsync(Uri uri, CancellationToken cancellationToken)
    {
        var core = _webView!.CoreWebView2;
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? navigationId = null;

        void OnStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
        {
            if (navigationId == null && args.Uri == uri.AbsoluteUri) navigationId = args.NavigationId;
        }

        void OnCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (args.NavigationId == navigationId) completion.TrySetResult(args);
        }

        core.NavigationStarting += OnStarting;
        core.NavigationCompleted += OnCompleted;
        try
        {
            core.Navigate(uri.AbsoluteUri);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            var args = await completion.Task;
            if (args.HttpStatusCode == 0)
            {
                LastError = Loc.ConsoleCreditsReadFailed(args.WebErrorStatus.ToString());
                return false;
            }
            if (!IsConsoleOrigin(core.Source))
            {
                LastError = Loc.ConsoleCreditsReadFailed("unexpected page");
                return false;
            }
            return true;
        }
        finally
        {
            core.NavigationStarting -= OnStarting;
            core.NavigationCompleted -= OnCompleted;
        }
    }

    /// <summary>지금 열린 platform.claude.com 문서 안에서 API 하나를 부른다. 응답을 못 받으면 상태 0.</summary>
    private async Task<ConsoleResponse> FetchAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var core = _webView?.CoreWebView2;
        if (core == null || !IsConsoleOrigin(core.Source)) return ConsoleResponse.NoResponse;

        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<ConsoleResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingFetches[id] = completion;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FetchTimeout);
            using var registration = timeout.Token.Register(() => completion.TrySetResult(ConsoleResponse.NoResponse));
            try
            {
                await core.ExecuteScriptAsync(BuildFetchScript(id, "https://platform.claude.com" + path));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 문서 교체 중이면 실행이 거절될 수 있다 — 다음 확인에서 다시 본다.
                Trace.TraceWarning($"Claude Console script could not run: {ex.Message}");
                return ConsoleResponse.NoResponse;
            }
            var response = await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            return response;
        }
        finally
        {
            _pendingFetches.Remove(id);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!IsConsoleOrigin(args.Source)) return;
        string message;
        try
        {
            message = args.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }
        if (OpenCodeWebUsageService.ParseFetchMessage(message) is { } fetched
            && _pendingFetches.Remove(fetched.Id, out var completion))
            completion.TrySetResult(fetched.Response);
    }

    /// <summary>
    /// 문서 안에서 실행할 fetch. 오류 응답도 본문 앞부분을 돌려준다 — 로그아웃(<see cref="SessionInvalidCode"/>)과
    /// 다른 403 을 가르기 위해서다. 주소는 절대 경로라 다른 사이트 문서에서 실행돼도 쿠키 없는 교차 출처 요청으로 끝난다.
    /// </summary>
    internal static string BuildFetchScript(string id, string url) => $$"""
        (() => {
          const id = {{JsonSerializer.Serialize(id)}};
          const post = (status, body) => {
            try { window.chrome.webview.postMessage(JSON.stringify({ id, status, body })); } catch (_) { }
          };
          fetch({{JsonSerializer.Serialize(url)}}, {
            credentials: "same-origin",
            cache: "no-store",
            headers: { "Accept": "application/json" },
            signal: AbortSignal.timeout({{(int)FetchTimeout.TotalMilliseconds}})
          })
            .then(r => r.text().then(body => post(r.status, r.status === 200 ? body : body.slice(0, 2000))))
            .catch(() => post(0, ""));
        })();
        """;

    private async Task EnsureWebViewAsync(bool interactive)
    {
        if (_webView?.CoreWebView2 != null) return;

        Directory.CreateDirectory(_dataRoot);
        _webView = new WebView2();
        _window = new Window
        {
            Title = Loc.ConsoleCreditsLoginTitle,
            Width = 920,
            Height = 760,
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
            _activeRead?.Cancel();
        };

        // WPF WebView2 는 초기화에 프레젠테이션 소스가 필요하다 — 화면 밖에서 잠깐 띄웠다가 숨긴다.
        _window.Show();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _dataRoot);
        await _webView.EnsureCoreWebView2Async(environment);
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        if (!interactive) _window.Hide();
    }

    /// <summary>Console API 를 쿠키와 함께 부를 수 있는 문서인가 — https://platform.claude.com 기본 포트만.</summary>
    internal static bool IsConsoleOrigin(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals("platform.claude.com", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort;

    internal static bool IsValidOrgId(string? orgId) => Guid.TryParseExact(orgId, "D", out _);

    internal static string? ReadOrgId(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var orgId = reader.ReadToEnd().Trim();
            return IsValidOrgId(orgId) ? orgId : null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Claude Console organization could not be read: {ex.Message}");
            return null;
        }
    }

    internal static void WriteOrgId(string path, string orgId)
    {
        if (!IsValidOrgId(orgId)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream))
                writer.Write(orgId);
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Claude Console organization could not be saved: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _gate.Dispose();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.Invoke(() =>
        {
            _allowClose = true;
            _activeRead?.Cancel();
            _webView?.Dispose();
            _window?.Close();
            _webView = null;
            _window = null;
        });
    }
}

internal readonly record struct CreditReadOutcome(
    ClaudeConsoleSessionState State, ClaudeConsoleCredits? Credits, string? OrgId, string? Error)
{
    public static CreditReadOutcome SignedOut => new(ClaudeConsoleSessionState.SignedOut, null, null, null);
    public static CreditReadOutcome Ok(ClaudeConsoleCredits? credits, string orgId) =>
        new(ClaudeConsoleSessionState.Ok, credits, orgId, null);
    public static CreditReadOutcome Failed(string error) => new(ClaudeConsoleSessionState.Unavailable, null, null, error);
}
