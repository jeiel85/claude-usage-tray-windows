using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class CredentialService : IDisposable
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly string DefaultCredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(AppConstants.AuthTimeoutSeconds) };

    private readonly string _credentialsPath;
    private readonly HttpClient _http;
    private readonly FileSystemWatcher? _watcher;

    // 토큰 갱신 실패 백오프 상태(#177). 실패한 refresh 토큰을 기억해, 재로그인으로 파일의 토큰이 바뀌면 즉시 푼다.
    private volatile TokenRefreshFailure? _refreshFailure;
    private string? _failedRefreshToken;
    private int _consecutiveRefreshFailures;

    /// <summary>
    /// credentials.json 이 변경되면 발생 (계정 전환 감지용).
    /// 쓰기 완료 후 최소 500ms 지연을 두어 파일 잠금 방지.
    /// </summary>
    public event Action? CredentialsChanged;

    /// <summary>
    /// 마지막 토큰 갱신 실패와 다음 재시도 시각. 유효한 토큰을 읽었거나 갱신에 성공하면 null.
    /// 만료 토큰으로 401 을 받은 호출부가 원인(일시 제한·재로그인 필요)을 안내하는 데 쓴다.
    /// </summary>
    public TokenRefreshFailure? RefreshFailure => _refreshFailure;

    /// <param name="credentialsPath">자격 파일 경로. null 이면 ~/.claude/.credentials.json (테스트용 주입점).</param>
    /// <param name="tokenHandler">토큰 엔드포인트 HTTP 핸들러. null 이면 공유 클라이언트 (테스트용 주입점).</param>
    public CredentialService(string? credentialsPath = null, HttpMessageHandler? tokenHandler = null)
    {
        _credentialsPath = credentialsPath ?? DefaultCredentialsPath;
        _http = tokenHandler is null
            ? SharedHttp
            : new HttpClient(tokenHandler) { Timeout = TimeSpan.FromSeconds(AppConstants.AuthTimeoutSeconds) };

        var dir = Path.GetDirectoryName(_credentialsPath)!;
        if (Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(_credentialsPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            // Changed/Created/Deleted/Renamed 이벤트 모두 감지
            // Electron 앱은 atomic write(임시파일 → rename) 방식으로 저장하므로 Renamed 필수
            _watcher.Changed += OnCredentialsFileChanged;
            _watcher.Created += OnCredentialsFileChanged;
            _watcher.Deleted += OnCredentialsFileChanged;
            _watcher.Renamed += (s, e) => OnCredentialsFileChanged(s, e);
        }
    }

    private System.Timers.Timer? _debounceTimer;
    private volatile bool _isSelfWriting = false;

    private void OnCredentialsFileChanged(object sender, FileSystemEventArgs e)
    {
        // 앱 자체가 토큰 갱신으로 쓴 경우 무시 — self-triggered loop 방지
        if (_isSelfWriting) return;

        // 500ms debounce — 파일 저장 중 여러 이벤트 방지
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Timers.Timer(AppConstants.FileWriteDebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => CredentialsChanged?.Invoke();
        _debounceTimer.Start();
    }

    public ClaudeCredentials? Load()
    {
        TryLoad(out var credentials);
        return credentials;
    }

    /// <summary>
    /// 자격 파일을 읽는다. 반환값은 "판단할 수 있었는가" 다 — 파일이 없으면 true(자격 없음이 확정),
    /// 파일은 있는데 읽기·파싱에 실패하면 false(Claude Code 가 쓰는 도중일 수 있어 판단 보류).
    /// 호출부는 false 일 때 직전 판단을 유지해야 한다.
    /// </summary>
    private bool TryLoad(out ClaudeCredentials? credentials)
    {
        credentials = null;
        if (!File.Exists(_credentialsPath)) return true;
        try
        {
            using var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            credentials = JsonSerializer.Deserialize<ClaudeCredentials>(stream);
            return credentials is not null;
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[CredentialService] Load failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return false;
        }
    }

    public string? GetAccessToken()
    {
        var cred = Load();
        return NullIfBlank(cred?.ClaudeAiOauth?.AccessToken);
    }

    /// <summary>
    /// Claude Code 가 로그아웃/자격 이관 시 accessToken 을 <b>빈 문자열</b>로 남기고 나머지 필드
    /// (scopes, subscriptionType 등)만 유지하는 경우가 있다. 빈 값을 그대로 흘려보내면 호출부가
    /// "토큰 있음"으로 오해해 인증 없는 요청을 보내고, 그 응답(429/401)을 일시적 제한으로
    /// 오진하게 된다. 여기서 없음(null)으로 정규화한다.
    /// </summary>
    private static string? NullIfBlank(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token;

    public string? GetOrganizationUuid() => Load()?.OrganizationUuid;

    public bool HasCredentials() => File.Exists(_credentialsPath);

    /// <summary>
    /// 자격 파일의 마지막 수정 시각(UTC). 파일이 없으면 null. 파일 감시자가 없는 PC 에서도 재로그인 완료를
    /// 알아챌 수 있도록 "터미널에서 로그인" 뒤의 변경 감지에 쓴다(#180).
    /// </summary>
    public DateTime? GetLastWriteTimeUtc() =>
        File.Exists(_credentialsPath) ? File.GetLastWriteTimeUtc(_credentialsPath) : null;

    /// <summary>
    /// 구독 등급 표시에 필요한 두 값을 한 번의 파일 읽기로 돌려준다. Max 는 5x/20x 로 한도가 갈리는데
    /// 그 배수는 subscriptionType 이 아니라 rateLimitTier("default_claude_max_5x") 에만 들어 있다.
    /// 파일이 있는데 읽지 못한 경우(쓰기 도중 등) false 를 돌려준다 — 일시적인 읽기 실패를 "구독 아님" 으로
    /// 뒤집지 않으려는 것이다. 파일이 없으면 true 와 (null, null) — 로그아웃이 확정이다.
    /// </summary>
    public bool TryGetSubscriptionInfo(out (string? SubscriptionType, string? RateLimitTier) info)
    {
        var ok = TryLoad(out var credentials);
        info = (credentials?.ClaudeAiOauth?.SubscriptionType, credentials?.ClaudeAiOauth?.RateLimitTier);
        return ok;
    }

    /// <summary>
    /// Returns a valid access token, refreshing it first if it has expired.
    /// Returns null if no credentials exist. If refresh fails (or is backing off after a failure),
    /// falls back to the stored token and records the reason in <see cref="RefreshFailure"/>.
    /// Thread-safe: serializes concurrent refresh attempts.
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var cred = Load();
            if (cred?.ClaudeAiOauth is not { } oauth) return null;

            // Token still valid (with 60s buffer)
            if (!oauth.IsExpired &&
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < oauth.ExpiresAt - 60_000)
            {
                ClearRefreshFailure();
                return NullIfBlank(oauth.AccessToken);
            }

            // 재로그인 등으로 refresh 토큰이 바뀌었으면 이전 실패의 백오프는 이 토큰과 무관하다.
            if (_failedRefreshToken is not null && _failedRefreshToken != oauth.RefreshToken)
                ClearRefreshFailure();

            // 백오프 중에는 토큰 엔드포인트를 두드리지 않는다 — 매 폴링마다 재시도하면 429 가 풀리지 않는다(#177).
            if (_refreshFailure is { } pending && DateTimeOffset.UtcNow < pending.RetryAtUtc)
                return NullIfBlank(oauth.AccessToken);

            // Try to refresh
            var (refreshed, failure) = await TryRefreshAsync(oauth.RefreshToken, oauth.Scopes);
            if (refreshed is null)
            {
                if (failure is not null) RecordRefreshFailure(failure.Value, oauth.RefreshToken);
                return NullIfBlank(oauth.AccessToken); // fall back to existing token
            }
            ClearRefreshFailure();

            // Persist updated credentials
            try
            {
                oauth.AccessToken = refreshed.AccessToken;
                oauth.ExpiresAt   = refreshed.ExpiresAt;
                if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                    oauth.RefreshToken = refreshed.RefreshToken;

                // Merge back — preserve other fields in the JSON
                var raw = JsonNode.Parse(File.ReadAllText(_credentialsPath))!;
                raw["claudeAiOauth"]!["accessToken"] = oauth.AccessToken;
                raw["claudeAiOauth"]!["expiresAt"]   = oauth.ExpiresAt;
                if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                    raw["claudeAiOauth"]!["refreshToken"] = oauth.RefreshToken;

                // 자가 쓰기 플래그 설정 — FileSystemWatcher self-trigger 방지
                _isSelfWriting = true;
                try
                {
                    File.WriteAllText(_credentialsPath,
                        raw.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                }
                finally
                {
                    // 파일시스템 이벤트가 드레인될 시간 확보 후 플래그 해제
                    // 최대 2초 대기 후에도 플래그 해제 (timeout safety)
                    _ = ResetSelfWriteFlagAsync();
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[CredentialService] Save failed: {ex.Message}");
#endif
                GC.KeepAlive(ex);
                /* ignore write errors */
            }

            return NullIfBlank(oauth.AccessToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 파일쓰기 후 플래그를 안전하게 해제.
    /// 즉시 해제 시 FileSystemWatcher가 아직 이벤트를 처리 중이면 무한 루프 발생.
    /// 최대 2초 대기 후에도 플래그 해제 (timeout safety).
    /// </summary>
    private async Task ResetSelfWriteFlagAsync()
    {
        const int debounceMs = 800;  // FileSystemWatcher debounce + buffer
        const int maxWaitMs = 2000; // Safety timeout

        await Task.Delay(debounceMs);

        // Safety: 최대 대기시간 후에도 플래그 해제
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs - debounceMs);
        while (_isSelfWriting && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        _isSelfWriting = false;
    }

    private void ClearRefreshFailure()
    {
        _refreshFailure = null;
        _failedRefreshToken = null;
        _consecutiveRefreshFailures = 0;
    }

    private void RecordRefreshFailure(RefreshAttemptFailure attempt, string? refreshToken)
    {
        int delaySeconds;
        if (attempt.ReachedServer)
        {
            _consecutiveRefreshFailures++;
            delaySeconds = ComputeRefreshBackoffSeconds(_consecutiveRefreshFailures, attempt.RetryAfterSeconds);
        }
        else
        {
            // 서버에 닿지 못한 실패(네트워크 끊김 등)는 서버 부하가 없으므로 짧게 다시 시도한다.
            delaySeconds = AppConstants.TokenRefreshNetworkRetrySeconds;
        }
        var kind = ResolveFailureKind(_refreshFailure?.Kind, _failedRefreshToken, refreshToken, attempt.Kind);
        _failedRefreshToken = refreshToken;
        _refreshFailure = new TokenRefreshFailure(kind, DateTimeOffset.UtcNow.AddSeconds(delaySeconds));
    }

    /// <summary>
    /// 같은 refresh 토큰이 한 번 invalid_grant 로 거절됐으면, 이후 재시도가 429 등으로 실패해도 Rejected 를 유지한다
    /// — 영구적인 문제(재로그인 필요)가 일시 제한 안내로 가려지지 않도록.
    /// </summary>
    internal static TokenRefreshFailureKind ResolveFailureKind(
        TokenRefreshFailureKind? previousKind, string? previousToken, string? refreshToken, TokenRefreshFailureKind newKind) =>
        previousKind == TokenRefreshFailureKind.Rejected && previousToken == refreshToken
            ? TokenRefreshFailureKind.Rejected
            : newKind;

    /// <summary>
    /// 서버가 거절한 갱신의 다음 재시도까지 대기 시간(초). 5분에서 시작해 연속 실패마다 두 배, 60분 상한.
    /// 서버가 Retry-After 를 주면 그보다 일찍 재시도하지 않되, 6시간을 넘겨 기다리지는 않는다
    /// (안내는 HH:mm 만 표시하므로 하루 넘는 대기는 오늘 시각처럼 읽힌다).
    /// </summary>
    internal static int ComputeRefreshBackoffSeconds(int consecutiveFailures, int? retryAfterSeconds)
    {
        var exponent = Math.Clamp(consecutiveFailures - 1, 0, 10);
        var backoff = (int)Math.Min((long)AppConstants.TokenRefreshBackoffBaseSeconds << exponent,
            AppConstants.TokenRefreshBackoffMaxSeconds);
        return retryAfterSeconds is > 0
            ? Math.Max(backoff, Math.Min(retryAfterSeconds.Value, AppConstants.TokenRefreshRetryAfterMaxSeconds))
            : backoff;
    }

    internal static TokenRefreshFailureKind ClassifyRefreshFailure(int statusCode, string? body)
    {
        if (statusCode == 429) return TokenRefreshFailureKind.RateLimited;
        // invalid_grant: refresh 토큰이 폐기·만료됨 — 다시 로그인해야 한다.
        if (statusCode is 400 or 401 && body?.Contains("invalid_grant", StringComparison.Ordinal) == true)
            return TokenRefreshFailureKind.Rejected;
        return TokenRefreshFailureKind.Failed;
    }

    private static int? ParseRetryAfterSeconds(System.Net.Http.Headers.RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta) return (int)Math.Ceiling(delta.TotalSeconds);
        if (retryAfter?.Date is { } date) return (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds);
        return null;
    }

    private async Task<(int Status, string Body, int? RetryAfterSeconds)> PostRefreshAsync(
        string refreshToken, string[]? scopes)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"]     = ClientId
        };
        if (scopes is { Length: > 0 })
            payload["scope"] = string.Join(' ', scopes);

        using var response = await _http.SendAsync(new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        });
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body, ParseRetryAfterSeconds(response.Headers.RetryAfter));
    }

    private async Task<(RefreshResult? Result, RefreshAttemptFailure? Failure)> TryRefreshAsync(
        string? refreshToken, string[]? scopes)
    {
        if (string.IsNullOrEmpty(refreshToken)) return (null, null);
        var reachedServer = false;
        try
        {
            // Claude Code 와 같은 요청 모양 — 저장된 scope 를 그대로 다시 요청한다.
            var reply = await PostRefreshAsync(refreshToken, scopes);
            reachedServer = true;

            // scope 를 실어 보낸 요청이 400(invalid_grant 아님)으로 거부되면 #177 이전처럼 scope 없이 한 번 더 —
            // 서버가 저장된 scope 조합을 받지 않게 돼도 갱신이 영구히 막히지 않게 한다.
            if (scopes is { Length: > 0 } && reply.Status == 400
                && ClassifyRefreshFailure(reply.Status, reply.Body) != TokenRefreshFailureKind.Rejected)
                reply = await PostRefreshAsync(refreshToken, null);

            var json = reply.Body;
            if (reply.Status is < 200 or > 299)
            {
                var kind = ClassifyRefreshFailure(reply.Status, json);
                return (null, new RefreshAttemptFailure(kind, reply.RetryAfterSeconds, true));
            }

            using var doc = JsonDocument.Parse(json);
            var root  = doc.RootElement;

            if (!root.TryGetProperty("access_token", out var at))
                return (null, new RefreshAttemptFailure(TokenRefreshFailureKind.Failed, null, true));

            long expiresAt;
            if (root.TryGetProperty("expires_in", out var expiresIn))
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + expiresIn.GetInt64() * 1000;
            else
                expiresAt = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeMilliseconds();

            string? newRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

            return (new RefreshResult(at.GetString()!, expiresAt, newRefresh), null);
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[CredentialService] Refresh failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return (null, new RefreshAttemptFailure(TokenRefreshFailureKind.Failed, null, reachedServer));
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        if (!ReferenceEquals(_http, SharedHttp)) _http.Dispose();
        GC.SuppressFinalize(this);
    }

    private record RefreshResult(string AccessToken, long ExpiresAt, string? RefreshToken);

    private readonly record struct RefreshAttemptFailure(
        TokenRefreshFailureKind Kind, int? RetryAfterSeconds, bool ReachedServer);
}

public enum TokenRefreshFailureKind
{
    /// <summary>토큰 엔드포인트가 429 로 일시 제한.</summary>
    RateLimited,
    /// <summary>refresh 토큰이 거절됨(invalid_grant) — 다시 로그인해야 한다.</summary>
    Rejected,
    /// <summary>그 밖의 실패(서버 오류·네트워크 오류 등).</summary>
    Failed
}

/// <summary>토큰 갱신 실패 종류와, 백오프가 끝나 다시 갱신을 시도할 시각(UTC).</summary>
public sealed record TokenRefreshFailure(TokenRefreshFailureKind Kind, DateTimeOffset RetryAtUtc);
