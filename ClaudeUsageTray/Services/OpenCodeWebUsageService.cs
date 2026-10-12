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
/// <remarks>
/// OpenCode Go 는 2026-09-22 새 콘솔(<c>https://opencode.ai/console</c>)로 옮겨졌다
/// (anomalyco/opencode#50716). 레거시 <c>/workspace/&lt;id&gt;/go</c> 는 무조건 <c>/console/go</c> 로
/// 보내고, 새 콘솔은 빈 SPA 셸이라 예전처럼 페이지 HTML 에서 값을 긁을 수 없다. 대신 콘솔이 쓰는
/// JSON API 를 앱 전용 WebView2 의 opencode.ai 문서 안에서 <c>fetch</c> 로 불러, 쿠키가 앱 밖으로
/// 나가지 않게 한다.
/// </remarks>
public sealed class OpenCodeWebUsageService : IDisposable
{
    internal static readonly TimeSpan CachedFallbackMaxAge = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan SignedOutRetryDelay = TimeSpan.FromMinutes(30);
    /// <summary>확인 실패의 재시도 간격 단계 — 1·2·4·8·16분.</summary>
    private const int MaxUnavailableBackoffSteps = 5;
    /// <summary>로그인 창에 보여줄 새 콘솔의 Go 페이지. 비로그인이면 콘솔이 알아서 로그인 화면으로 보낸다.</summary>
    internal static readonly Uri ConsoleGoUri = new("https://opencode.ai/console/go");
    /// <summary>
    /// 자동 조회가 여는 문서. 스크립트가 없는 JSON 응답이라 콘솔 SPA 처럼 문서를 갈아치우지 않아
    /// 그 안에서 실행한 fetch 의 답이 중간에 끊기지 않는다(비로그인이어도 401 JSON 문서가 열린다).
    /// </summary>
    internal static readonly Uri ConsoleOrgsUri = new("https://opencode.ai/console/api/orgs");
    internal const string OrgsPath = "/orgs";
    internal const string GoStatusPath = "/go/status";
    /// <summary>작업공간 안에서 내 역할(member·admin·owner). 콘솔은 크레딧을 관리자에게만 보여준다.</summary>
    internal const string CurrentOrgPath = "/orgs/current";
    internal const string BillingStatusPath = "/billing/status";
    /// <summary>1 USD = 100 센트 = 100,000,000 마이크로센트.</summary>
    private const decimal MicroCentsPerDollar = 100_000_000m;
    private const string ConsoleApiBase = "https://opencode.ai/console/api";
    /// <summary>콘솔이 작업공간을 고르는 헤더. 없으면 go/status 가 400 으로 답한다.</summary>
    private const string OrgHeader = "x-org-id";
    /// <summary>Go 는 1인 작업공간 전용이라 보통 1~2개면 찾는다. 계정이 작업공간을 많이 가져도 조회를 끝내기 위한 상한.</summary>
    private const int MaxOrgsToProbe = 10;
    private static readonly TimeSpan ConsoleFetchTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SignInPollInterval = TimeSpan.FromSeconds(2);
    /// <summary>롤링 창 길이. 첫 사용부터 시작하는 창이라, 시작 전이면 콘솔이 초기화 시각을 비워 보낸다.</summary>
    private static readonly TimeSpan RollingWindow = TimeSpan.FromHours(5);
    private static readonly Regex OrgIdPattern = new(@"^(?:org|wrk)_[A-Za-z0-9_-]{1,128}$",
        RegexOptions.CultureInvariant);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _dataRoot;
    private readonly string _orgIdPath;
    private readonly Dictionary<string, TaskCompletionSource<ConsoleResponse>> _pendingFetches = new();
    private Window? _window;
    private WebView2? _webView;
    private bool _allowClose;
    private volatile bool _disposed;
    private CancellationTokenSource? _activeRead;
    private OpenCodeWebUsage? _cachedUsage;
    private DateTimeOffset _cacheExpiresAt;
    private DateTimeOffset _retryAfter;
    private OpenCodeWebSessionState _lastFailureState = OpenCodeWebSessionState.Unknown;
    /// <summary>마지막 판정이 NoQuota 였을 때 그 이유(일시정지 등). 재시도를 기다리는 동안에도 같은 안내를 유지한다.</summary>
    private OpenCodeGoSubscriptionStatus? _lastInactive;
    private int _consecutiveUnavailable;
    private string? _orgId;

    public string? LastError { get; private set; }
    internal string? SavedOrgId => _orgId;

    /// <summary>
    /// 이 PC 에서 로그인에 성공해 Go 작업공간까지 저장해 둔 적이 있는가.
    /// 작업공간 파일은 공식 값을 실제로 읽어낸 뒤에만 쓰이므로 "여기서 한 번은 됐다" 의 증거가 된다.
    /// 레거시 콘솔 시절의 workspace-route.txt 는 새 콘솔 세션과 무관하므로 증거로 쓰지 않는다.
    /// </summary>
    public bool HasSavedSession => _orgId != null;

    public OpenCodeWebUsageService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeUsageTray", "OpenCodeWebSession"))
    {
    }

    internal OpenCodeWebUsageService(string dataRoot)
    {
        _dataRoot = dataRoot;
        _orgIdPath = Path.Combine(dataRoot, "console-org.txt");
        _orgId = ReadOrgId(_orgIdPath);
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
                _lastInactive = null;
                _consecutiveUnavailable = 0;
                return result;
            }
            return interactive ? result : RegisterFailure(result.State, DateTimeOffset.Now, result.Inactive);
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
            // 진행 중이던 조회는 Dispose 의 취소로 끝나며 여기에 늦게 도착할 수 있다.
            if (!_disposed) _gate.Release();
        }
    }

    /// <summary>
    /// 실패를 기록하고 다음 확인 시각을 잡는다. 로그인이 풀렸거나 Go 할당량이 없는 경우엔 사용자가
    /// 로그인·구독하기 전까지 결과가 달라지지 않으므로 길게 쉬고, 확인 자체를 못 한 경우엔 짧게 쉬었다가 다시 본다 —
    /// 부팅 직후 네트워크가 늦게 올라온 것뿐인데 30분 동안 로그인 버튼을 띄우고 있지 않도록.
    /// </summary>
    private OpenCodeWebReadResult RegisterFailure(
        OpenCodeWebSessionState state, DateTimeOffset now, OpenCodeGoSubscriptionStatus? inactive = null)
    {
        if (state is OpenCodeWebSessionState.Unknown or OpenCodeWebSessionState.Authenticated)
            state = OpenCodeWebSessionState.Unavailable;

        _lastFailureState = state;
        _lastInactive = state == OpenCodeWebSessionState.NoQuota ? inactive : null;
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
            : new OpenCodeWebReadResult(null, _lastFailureState, _lastInactive);

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
        _activeRead = cts;
        try
        {
            return interactive
                ? await WaitForSignInAsync(cts.Token)
                : await ReadOnceAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 로그인 창을 열어둔 동안의 시간 초과·창 닫기는 "사용자가 안 끝냈다" 이므로 종전 문구를 남긴다.
            if (!interactive) LastError ??= Loc.OpenCodeWebNavigationTimedOut;
            return OpenCodeWebReadResult.Unavailable;
        }
        finally
        {
            _activeRead = null;
            foreach (var pending in _pendingFetches.Values) pending.TrySetResult(ConsoleResponse.NoResponse);
            _pendingFetches.Clear();
            // 앱 종료(Dispose)로 취소됐으면 창은 이미 닫혀 null 이다.
            if (_window is { IsVisible: true } window) window.Hide();
        }
    }

    /// <summary>자동 조회 — 스크립트 없는 콘솔 API 문서를 열고, 그 안에서 할당량을 한 번 읽는다.</summary>
    private async Task<OpenCodeWebReadResult> ReadOnceAsync(CancellationToken cancellationToken)
    {
        if (!await OpenConsoleDocumentAsync(ConsoleOrgsUri, cancellationToken))
            return OpenCodeWebReadResult.Unavailable;

        var outcome = await ReadQuotaAsync(
            (path, orgId) => FetchConsoleAsync(path, orgId, cancellationToken), _orgId, DateTimeOffset.Now);
        switch (outcome.Kind)
        {
            case ConsoleReadKind.Success:
                return Succeed(outcome);
            case ConsoleReadKind.SignedOut:
                // 작업공간 목록이 401 로 답했다 — 세션이 실제로 만료된 유일한 신호다.
                return OpenCodeWebReadResult.SignedOut;
            case ConsoleReadKind.NoQuota:
                // 로그인은 됐지만 Go 할당량이 없다. 확인 실패로 뭉개면 "곧 다시 시도" 안내 뒤로 이유가 숨는다.
                LastError = outcome.Error;
                return OpenCodeWebReadResult.NoQuotaBecause(outcome.Inactive);
            default:
                // 응답을 받지 못했거나 예상 밖의 응답이었다. 로그인 여부를 단정할 수 없으므로
                // 로그인 버튼을 띄우지 않고 확인 실패로만 남긴다.
                LastError = outcome.Error;
                return OpenCodeWebReadResult.Unavailable;
        }
    }

    /// <summary>
    /// 로그인 창 — 콘솔 Go 페이지를 보여주고, 사용자가 로그인을 마칠 때까지 같은 문서 안에서 주기적으로
    /// 할당량을 읽어 본다. 콘솔 SPA 는 로그인 전후로 문서를 통째로 바꾸므로(location.replace) 탐색 완료
    /// 이벤트 대신 주기 확인을 쓰고, 그 사이 끊긴 응답은 다음 확인에서 다시 본다.
    /// </summary>
    private async Task<OpenCodeWebReadResult> WaitForSignInAsync(CancellationToken cancellationToken)
    {
        _webView!.CoreWebView2.Navigate(ConsoleGoUri.AbsoluteUri);
        while (true)
        {
            await Task.Delay(SignInPollInterval, cancellationToken);
            // GitHub·Google 같은 로그인 공급자 페이지에서는 확인하지 않는다.
            if (!IsConsoleOrigin(_webView.CoreWebView2.Source)) continue;

            var outcome = await ReadQuotaAsync(
                (path, orgId) => FetchConsoleAsync(path, orgId, cancellationToken), _orgId, DateTimeOffset.Now);
            if (outcome.Kind == ConsoleReadKind.Success) return Succeed(outcome);
            // 로그인했는데 Go 할당량이 없으면 창을 닫았을 때 그 이유를 보여준다. 로그인 전(401)이나
            // 문서 전환 중 끊긴 응답은 사용자가 아직 로그인 중이라는 뜻이므로 남기지 않는다.
            LastError = outcome.Kind == ConsoleReadKind.NoQuota ? outcome.Error : null;
        }
    }

    private OpenCodeWebReadResult Succeed(ConsoleReadOutcome outcome)
    {
        if (outcome.OrgId != null && outcome.OrgId != _orgId)
        {
            _orgId = outcome.OrgId;
            WriteOrgId(_orgIdPath, outcome.OrgId);
        }
        LastError = null;
        return OpenCodeWebReadResult.Success(outcome.Usage!);
    }

    /// <summary>
    /// 숨은 WebView2 에 문서를 열고 탐색 완료를 기다린다. 콘솔 API 의 401 은 IsSuccess=false 로 오지만
    /// 문서(JSON 본문)는 정상적으로 열린 것이고, 응답 코드조차 없을 때만 탐색 자체가 실패한 것이다 —
    /// 부팅 직후처럼 네트워크가 아직 준비되지 않은 경우로, 이걸 로그아웃과 같이 다루면 멀쩡한 세션에
    /// 로그인 버튼을 들이밀게 된다.
    /// </summary>
    private async Task<bool> OpenConsoleDocumentAsync(Uri uri, CancellationToken cancellationToken)
    {
        var core = _webView!.CoreWebView2;
        var completion = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? navigationId = null;

        // 직전 문서의 늦은 탐색이 끝나며 오는 완료 이벤트를 이번 탐색으로 착각하지 않도록 ID 로 짝짓는다.
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
            // Source dependency property에 현재 주소와 같은 값을 다시 넣으면 WPF가 변경으로 보지 않아
            // 탐색 이벤트가 발생하지 않는다(#130). CoreWebView2에 매번 명시적으로 탐색을 요청한다.
            core.Navigate(uri.AbsoluteUri);
            using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            var args = await completion.Task;
            if (args.HttpStatusCode == 0)
            {
                LastError = Loc.OpenCodeWebNavigationFailed(args.WebErrorStatus.ToString());
                return false;
            }
            if (!IsConsoleOrigin(core.Source))
            {
                LastError = Loc.OpenCodeWebQuotaNotFound(SafeSource(core.Source));
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

    /// <summary>
    /// 지금 열린 opencode.ai 문서 안에서 콘솔 API 하나를 부르고, 결과를 웹 메시지로 받는다.
    /// 응답을 못 받으면(네트워크·시간 초과·문서 전환) 상태 0 을 돌려준다.
    /// </summary>
    private async Task<ConsoleResponse> FetchConsoleAsync(string path, string? orgId, CancellationToken cancellationToken)
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
            timeout.CancelAfter(ConsoleFetchTimeout);
            using var registration = timeout.Token.Register(() => completion.TrySetResult(ConsoleResponse.NoResponse));
            try
            {
                await core.ExecuteScriptAsync(BuildFetchScript(id, ConsoleApiBase + path, orgId));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 문서 교체·렌더러 재시작 중 실행이 거절될 수 있다. 로그인 창이 이 한 번 때문에 닫히지 않도록
                // 응답 없음으로 돌려 다음 확인에서 다시 본다.
                Trace.TraceWarning($"OpenCode console script could not run: {ex.Message}");
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
        if (ParseFetchMessage(message) is { } fetched && _pendingFetches.Remove(fetched.Id, out var completion))
            completion.TrySetResult(fetched.Response);
    }

    /// <summary>
    /// 문서 안에서 실행할 fetch 스크립트. 값은 JSON 으로 넣어 따옴표가 섞여도 깨지지 않게 하고, 주소는
    /// 절대 경로라 혹시 다른 사이트 문서에서 실행돼도 쿠키 없이 교차 출처 요청으로 끝난다.
    /// </summary>
    internal static string BuildFetchScript(string id, string url, string? orgId)
    {
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (orgId != null) headers[OrgHeader] = orgId;
        return $$"""
            (() => {
              const id = {{JsonSerializer.Serialize(id)}};
              const post = (status, body) => {
                try { window.chrome.webview.postMessage(JSON.stringify({ id, status, body })); } catch (_) { }
              };
              fetch({{JsonSerializer.Serialize(url)}}, {
                credentials: "same-origin",
                cache: "no-store",
                headers: {{JsonSerializer.Serialize(headers)}},
                signal: AbortSignal.timeout({{(int)ConsoleFetchTimeout.TotalMilliseconds}})
              })
                .then(r => r.text().then(body => post(r.status, r.status === 200 ? body : "")))
                .catch(() => post(0, ""));
            })();
            """;
    }

    internal static (string Id, ConsoleResponse Response)? ParseFetchMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return null;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.Number
                || !status.TryGetInt32(out var code))
                return null;
            var body = root.TryGetProperty("body", out var text) && text.ValueKind == JsonValueKind.String
                ? text.GetString() ?? ""
                : "";
            return (id.GetString()!, new ConsoleResponse(code, body));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 콘솔 API 로 Go 할당량을 읽는다. 저장된 작업공간이 있으면 먼저 보고, 없거나 더 이상 Go 가 없으면
    /// 작업공간 목록에서 Go 할당량이 있는 곳을 찾는다. 로그아웃 판정은 작업공간 헤더 없이 부르는
    /// 작업공간 목록의 401 로만 한다 — 특정 작업공간의 401 은 그 작업공간에서 빠졌거나 다른 계정으로
    /// 로그인한 경우일 수 있어, 멀쩡한 세션을 로그아웃으로 오판하지 않도록 목록으로 다시 확인한다.
    /// </summary>
    internal static async Task<ConsoleReadOutcome> ReadQuotaAsync(
        Func<string, string?, Task<ConsoleResponse>> fetch, string? savedOrgId, DateTimeOffset now)
    {
        var checkedOrgs = new HashSet<string>(StringComparer.Ordinal);
        string? failure = null;
        var sawUnreadableQuota = false;
        OpenCodeGoSubscriptionStatus? inactive = null;
        if (savedOrgId != null)
        {
            var saved = await fetch(GoStatusPath, savedOrgId);
            if (saved.Status == 200)
            {
                if (ParseGoStatus(saved.Body, now) is { } usage)
                    return ConsoleReadOutcome.Success(await ApplyBillingAsync(fetch, savedOrgId, usage), savedOrgId);
                sawUnreadableQuota |= HasGoAccess(saved.Body);
                inactive ??= ParseInactiveStatus(saved.Body);
            }
            // 일시 오류(응답 없음·5xx)였다면 목록을 받은 뒤 한 번 더 묻는다.
            if (saved.Status is 200 or 401 or 403 or 404) checkedOrgs.Add(savedOrgId);
        }

        var orgs = await fetch(OrgsPath, null);
        if (orgs.Status == 401) return ConsoleReadOutcome.SignedOut;
        if (orgs.Status != 200) return ConsoleReadOutcome.Failed(DescribeFailure(orgs.Status));

        foreach (var orgId in ParseOrgIds(orgs.Body))
        {
            if (!checkedOrgs.Add(orgId)) continue;
            if (checkedOrgs.Count > MaxOrgsToProbe) break;

            var status = await fetch(GoStatusPath, orgId);
            if (status.Status != 200)
            {
                failure ??= DescribeFailure(status.Status);
                continue;
            }
            if (ParseGoStatus(status.Body, now) is { } usage)
                return ConsoleReadOutcome.Success(await ApplyBillingAsync(fetch, orgId, usage), orgId);
            sawUnreadableQuota |= HasGoAccess(status.Body);
            inactive ??= ParseInactiveStatus(status.Body);
        }

        // 접근 정보는 있는데 못 읽었으면 콘솔 응답 형식이 바뀐 것이다 — 미구독 문구로 덮지 않는다.
        if (sawUnreadableQuota) return ConsoleReadOutcome.Failed(Loc.OpenCodeWebQuotaNotFound(GoStatusPath));
        if (failure != null) return ConsoleReadOutcome.Failed(failure);
        // 구독은 있는데 지금 못 쓰는 상태(일시정지 등)면 "미구독" 대신 그 이유를 알린다.
        return inactive is { } reason
            ? ConsoleReadOutcome.NoQuota(Loc.OpenCodeGoInactive(reason), reason)
            : ConsoleReadOutcome.NoQuota(Loc.OpenCodeWebNoGoQuota);
    }

    /// <summary>
    /// 추가 사용·크레딧을 콘솔과 같은 규칙으로 채운다. 크레딧은 관리자만 볼 수 있어 역할을 먼저 묻고,
    /// 관리자에게는 쓸 크레딧이 남아 있을 때만 추가 사용을 켜짐으로 본다(역할을 모르면 콘솔처럼 멤버로 본다).
    /// 둘 다 덧붙이는 정보라 실패해도 할당량은 그대로 쓰고, 401 이어도 로그아웃으로 보지 않는다.
    /// 자동 조회 전체의 제한 시간이 이 단계에서 끝나거나 조회가 예외로 끝나도 이미 읽은 할당량은 버리지 않고,
    /// 결제 정보를 못 읽은 경우와 같게 다룬다.
    /// </summary>
    internal static async Task<OpenCodeWebUsage> ApplyBillingAsync(
        Func<string, string?, Task<ConsoleResponse>> fetch, string orgId, OpenCodeWebUsage usage)
    {
        var isAdmin = false;
        decimal? available = null;
        try
        {
            var current = await fetch(CurrentOrgPath, orgId);
            isAdmin = current.Status == 200 && IsAdminRole(ParseOrgRole(current.Body));
            if (!isAdmin) return usage;

            var billing = await fetch(BillingStatusPath, orgId);
            if (billing.Status == 200) available = ParseAvailableMicroCents(billing.Body);
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                Trace.TraceWarning($"OpenCode billing lookup failed: {ex.Message}");
            if (!isAdmin) return usage;
        }

        return usage with
        {
            ExtraUsageEnabled = usage.ExtraUsageEnabled && available > 0,
            CreditsUsd = available / MicroCentsPerDollar,
        };
    }

    private static bool IsAdminRole(string? role) =>
        role is not null
        && (role.Equals("admin", StringComparison.OrdinalIgnoreCase)
            || role.Equals("owner", StringComparison.OrdinalIgnoreCase));

    /// <summary><c>GET /console/api/orgs/current</c> 의 <c>role</c>. 못 읽으면 null.</summary>
    internal static string? ParseOrgRole(string? json) =>
        ParseRootObject(json, root => ReadString(root, "role"));

    /// <summary><c>GET /console/api/billing/status</c> 의 <c>availableMicroCents</c> — 콘솔 결제 화면의 "Available credits".</summary>
    internal static decimal? ParseAvailableMicroCents(string? json) =>
        ParseRootObject(json, root => ReadMicroCents(root, "availableMicroCents"));

    /// <summary>
    /// 구독은 있지만 지금 할당량을 못 쓰는 상태(<c>access: null</c>)의 이유. 구독이 없거나(<c>null</c>),
    /// 해지된 채 기간이 끝났거나(콘솔도 이때는 Go 카드를 감춘다), 할당량을 쓸 수 있으면 null.
    /// 구독 객체에는 요금제(<c>product</c>)가 늘 있으므로, 그것이 없으면 구독이 있다고 단정하지 않는다 —
    /// 근거 없이 "일시정지" 를 띄우면 미구독 계정에 "Go 를 구독한 계정으로 로그인" 안내가 가려진다.
    /// </summary>
    internal static OpenCodeGoSubscriptionStatus? ParseInactiveStatus(string? json) =>
        ParseRootObject(json, root =>
            root.TryGetProperty("access", out var access) && access.ValueKind == JsonValueKind.Null
            && !string.IsNullOrWhiteSpace(ReadString(root, "product"))
            && !IsTrue(root, "cancelAtPeriodEnd")
                ? ReadSubscriptionStatus(root, hasAccess: false)
                : (OpenCodeGoSubscriptionStatus?)null);

    /// <summary>콘솔 Go 카드의 상태 표시와 같은 순서로 판정한다(앞의 조건이 우선).</summary>
    private static OpenCodeGoSubscriptionStatus ReadSubscriptionStatus(JsonElement root, bool hasAccess)
    {
        if (!hasAccess)
        {
            if (HasValue(root, "renewalPaymentAttemptID")) return OpenCodeGoSubscriptionStatus.ApprovalNeeded;
            if (IsTrue(root, "renewalPending")) return OpenCodeGoSubscriptionStatus.RenewalProcessing;
            if (IsTrue(root, "renewalAuthorizationRequired")) return OpenCodeGoSubscriptionStatus.ReauthorizationNeeded;
            return OpenCodeGoSubscriptionStatus.Paused;
        }

        if (HasValue(root, "renewalRetryAt")) return OpenCodeGoSubscriptionStatus.PaymentFailed;
        if (IsTrue(root, "renewalAuthorizationRequired")) return OpenCodeGoSubscriptionStatus.ActionNeeded;
        if (IsTrue(root, "cancelAtPeriodEnd")) return OpenCodeGoSubscriptionStatus.Ends;
        if (ReadString(root, "product") == "go-plus" && ReadString(root, "renewalProduct") == "go")
            return OpenCodeGoSubscriptionStatus.SwitchesToGo;
        return OpenCodeGoSubscriptionStatus.Renews;
    }

    private static T? ParseRootObject<T>(string? json, Func<JsonElement, T?> read)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string DescribeFailure(int status) =>
        Loc.OpenCodeWebNavigationFailed(status == 0 ? "no response" : $"HTTP {status}");

    /// <summary>
    /// <c>GET /console/api/go/status</c> 응답을 롤링·주간·월간 창으로 바꾼다. 콘솔 화면과 같이
    /// 사용률은 <c>usedMicroCents / limitMicroCents</c> 이고, 마이크로센트는 문자열(bigint)로 온다.
    /// 구독이 없으면 <c>null</c>, 일시정지·결제 대기면 <c>access: null</c> 이라 할당량이 없다.
    /// </summary>
    internal static OpenCodeWebUsage? ParseGoStatus(string? json, DateTimeOffset observedAt)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetObject(document.RootElement, "access", out var access)
                || !TryGetObject(access, "meters", out var meters))
                return null;

            // 롤링 창은 첫 사용부터 시작해 아직 쓰지 않았으면 초기화 시각이 비어 온다. 레거시 콘솔이 같은
            // 상황에서 돌려주던 값(0%, 지금부터 5시간 뒤)과 맞춘다.
            var rolling = ParseMeter(meters, "fiveHour", observedAt.Add(RollingWindow));
            var weekly = ParseMeter(meters, "week", null);
            // 월간 창의 초기화는 구독 기간 끝과 같다. 응답에서 빠지면 그 값으로 대신한다.
            var monthly = ParseMeter(meters, "month", ReadDate(access, "endsAt"));
            if (rolling == null || weekly == null || monthly == null) return null;

            var root = document.RootElement;
            return new OpenCodeWebUsage
            {
                ObservedAtUtc = observedAt.ToUniversalTime(),
                Rolling = rolling,
                Weekly = weekly,
                Monthly = monthly,
                Product = ReadString(root, "product"),
                // 멤버 기준 값이다. 관리자면 ApplyBillingAsync 가 남은 크레딧까지 보고 다시 정한다.
                ExtraUsageEnabled = IsTrue(root, "useBalance"),
                Subscription = ReadSubscriptionStatus(root, hasAccess: true),
                PeriodEndsAt = ReadDate(access, "endsAt"),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static bool HasGoAccess(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryGetObject(document.RootElement, "access", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<string> ParseOrgIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];
            var ids = new List<string>();
            foreach (var org in document.RootElement.EnumerateArray())
            {
                if (org.ValueKind == JsonValueKind.Object
                    && org.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    && id.GetString() is { } value && IsValidOrgId(value) && !ids.Contains(value))
                    ids.Add(value);
            }
            return ids;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static OpenCodeQuotaWindow? ParseMeter(JsonElement meters, string name, DateTimeOffset? resetFallback)
    {
        if (!TryGetObject(meters, name, out var meter)) return null;
        var limit = ReadMicroCents(meter, "limitMicroCents");
        var used = ReadMicroCents(meter, "usedMicroCents");
        var resetAt = ReadDate(meter, "resetsAt") ?? resetFallback;
        if (limit is not { } limitValue || limitValue <= 0
            || used is not { } usedValue || usedValue < 0
            || resetAt is not { } reset)
            return null;

        return new OpenCodeQuotaWindow
        {
            UsagePercent = (double)Math.Min(1m, usedValue / limitValue),
            ResetAt = reset
        };
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value)
    {
        value = default;
        return parent.ValueKind == JsonValueKind.Object
            && parent.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static decimal? ReadMicroCents(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToLocalTime()
            : null;

    private static string? ReadString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool IsTrue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>콘솔이 참/거짓으로만 보는 선택 필드(재시도 시각·결제 시도 ID) — 비어 있지 않은 값이 있는가.</summary>
    private static bool HasValue(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value)
        && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False)
        && !(value.ValueKind == JsonValueKind.String && value.GetString() is "");

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
            _activeRead?.Cancel();
        };

        // WPF WebView2 needs a presentation source for initialization. The off-screen window is
        // shown only long enough to create the control, then hidden until the user chooses login.
        _window.Show();
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _dataRoot);
        await _webView.EnsureCoreWebView2Async(environment);
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false;
        _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        if (!interactive) _window.Hide();
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

    /// <summary>콘솔 API 를 쿠키와 함께 부를 수 있는 문서인가 — https://opencode.ai 의 기본 포트만 허용한다.</summary>
    internal static bool IsConsoleOrigin(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals("opencode.ai", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort;

    private static string SafeSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) ? $"{uri.Host}{uri.AbsolutePath}" : "?";

    internal static bool IsValidOrgId(string? orgId) => orgId != null && OrgIdPattern.IsMatch(orgId);

    internal static string? ReadOrgId(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var orgId = reader.ReadToEnd().Trim();
            return IsValidOrgId(orgId) ? orgId : null;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"OpenCode console workspace could not be read: {ex.Message}");
            return null;
        }
    }

    internal static bool WriteOrgId(string path, string orgId)
    {
        if (!IsValidOrgId(orgId)) return false;

        string? tempPath = null;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(stream))
                writer.Write(orgId);
            File.Move(tempPath, path, true);
            tempPath = null;
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"OpenCode console workspace could not be saved: {ex.Message}");
            return false;
        }
        finally
        {
            if (tempPath != null)
            {
                try { File.Delete(tempPath); }
                catch (Exception ex) { Trace.TraceWarning($"OpenCode workspace temp file could not be removed: {ex.Message}"); }
            }
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

/// <summary>콘솔 API 한 번의 응답. 네트워크·시간 초과·문서 전환으로 응답을 못 받았으면 상태 0.</summary>
internal readonly record struct ConsoleResponse(int Status, string Body)
{
    public static ConsoleResponse NoResponse => new(0, "");
}

internal enum ConsoleReadKind
{
    Success,
    /// <summary>콘솔 API 가 401 — 로그인이 풀렸다.</summary>
    SignedOut,
    /// <summary>로그인은 됐지만 Go 할당량이 있는 작업공간이 없다(미구독·일시정지).</summary>
    NoQuota,
    /// <summary>응답을 못 받았거나 예상 밖의 응답이었다.</summary>
    Failed,
}

internal readonly record struct ConsoleReadOutcome(
    ConsoleReadKind Kind, OpenCodeWebUsage? Usage, string? OrgId, string? Error,
    OpenCodeGoSubscriptionStatus? Inactive = null)
{
    public static ConsoleReadOutcome SignedOut => new(ConsoleReadKind.SignedOut, null, null, null);
    public static ConsoleReadOutcome Success(OpenCodeWebUsage usage, string orgId) =>
        new(ConsoleReadKind.Success, usage, orgId, null);
    public static ConsoleReadOutcome NoQuota(string error, OpenCodeGoSubscriptionStatus? inactive = null) =>
        new(ConsoleReadKind.NoQuota, null, null, error, inactive);
    public static ConsoleReadOutcome Failed(string error) => new(ConsoleReadKind.Failed, null, null, error);
}
