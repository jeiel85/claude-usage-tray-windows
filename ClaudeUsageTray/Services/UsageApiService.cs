using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class UsageApiService
{
    /// <summary>
    /// Sentinel <see cref="LastError"/> value set when no OAuth access token could be resolved
    /// (i.e. before any network call). ViewModels compare against this to surface an actionable
    /// login hint instead of the raw string. Non-localized on purpose — it's an internal marker.
    /// </summary>
    public const string NoTokenError = "No access token found";

    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private static readonly HttpClient SharedHttp = new() { Timeout = TimeSpan.FromSeconds(AppConstants.ApiTimeoutSeconds) };
    private readonly HttpClient _http;
    private readonly Func<Task<string?>> _tokenProvider;
    private readonly Func<DateTimeOffset> _clock;

    // 401·5xx 가 이어질 때의 쉬는 간격(#99). 첫 실패는 평소 주기(2분)대로 다시 묻고, 이어지면 4·8·16·30분.
    private static readonly TimeSpan[] FailureBackoffSteps =
        [TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(16), TimeSpan.FromMinutes(30)];
    private int _consecutiveFailures;
    private DateTimeOffset _failureRetryAt = DateTimeOffset.MinValue;
    private string? _failedToken;

    public string? LastRawResponse { get; private set; }
    public string? LastError { get; private set; }
    public int LastRetryAfterSeconds { get; private set; } = 0;

    public UsageApiService(CredentialService credentials)
        : this(credentials.GetValidAccessTokenAsync, SharedHttp)
    {
    }

    internal UsageApiService(Func<Task<string?>> tokenProvider, HttpClient http, Func<DateTimeOffset>? clock = null)
    {
        _tokenProvider = tokenProvider;
        _http = http;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 401·5xx 뒤 다시 묻지 않고 기다리는 중이면 그 시각(UTC), 아니면 null.
    /// 429·403 의 쿨다운(<see cref="LastRetryAfterSeconds"/>)과 달리 화면에 "잠시 제한" 안내를 띄울 일이 아니라 따로 둔다.
    /// </summary>
    public DateTimeOffset? FailureRetryAtUtc => _clock() < _failureRetryAt ? _failureRetryAt : null;

    public async Task<UsageResponse?> FetchUsageAsync()
    {
        var token = await _tokenProvider();
        // 빈 문자열도 "토큰 없음"이다 — 그대로 보내면 인증 없는 요청이 되어 429 로 돌아오고,
        // 호출부가 이를 일시적 rate limit 으로 오진한다.
        if (string.IsNullOrWhiteSpace(token))
        {
            LastError = NoTokenError;
            return null;
        }

        // 같은 토큰으로 401·5xx 가 이어지는 동안은 요청을 보내지 않고 지난 결과(LastError)를 그대로 둔다(#99).
        // 토큰이 바뀌면(Claude Code 가 갱신·재로그인) 기다리지 않는다 — 실패 원인이 그 토큰이었을 수 있다.
        if (token == _failedToken && _clock() < _failureRetryAt)
        {
            LastRetryAfterSeconds = 0;
            return null;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

            var response = await _http.SendAsync(request);
            LastRawResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastRetryAfterSeconds = 0;
                int statusCode = (int)response.StatusCode;
                if (statusCode == 429)
                {
                    // Retry-After 헤더가 없거나 파싱 실패해도 기본 backoff(300s)을 보장 — UI가 항상 쿨다운 분기로 라우팅되도록
                    if (response.Headers.TryGetValues("Retry-After", out var vals) &&
                        int.TryParse(vals.FirstOrDefault(), out var ra) && ra > 0)
                    {
                        LastRetryAfterSeconds = ra;
                    }
                    else
                    {
                        LastRetryAfterSeconds = AppConstants.DefaultRateLimitBackoffSeconds;
                    }
                }
                else if (statusCode == 403 && (LastRawResponse?.Contains("permission_error") ?? false))
                {
                    // 403 permission_error: 토큰 스코프 문제이므로 같은 결과를 반복 두드리지 않도록 긴 backoff
                    LastRetryAfterSeconds = AppConstants.PermissionDeniedBackoffSeconds;
                }
                else if (statusCode == 401 || statusCode >= 500)
                {
                    RecordFailure(token);
                }
                LastError = $"HTTP {statusCode}: {LastRawResponse}";
                return null;
            }

            ResetFailureBackoff();
            LastError = null;
            return JsonSerializer.Deserialize<UsageResponse>(LastRawResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            // 네트워크 오류·시간 초과는 쉬지 않는다 — 연결이 돌아오면 바로 다시 보여야 한다.
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>401·5xx 가 이어진 횟수만큼 다음 요청을 미룬다. 429·403 은 서버가 정한 쿨다운을 따로 쓴다.</summary>
    private void RecordFailure(string token)
    {
        if (token != _failedToken) _consecutiveFailures = 0;
        _failedToken = token;
        _consecutiveFailures++;
        var step = FailureBackoffSteps[Math.Min(_consecutiveFailures - 1, FailureBackoffSteps.Length - 1)];
        _failureRetryAt = _clock() + step;
    }

    /// <summary>
    /// 실패 백오프를 푼다. 성공했을 때와, 자격 파일이 바뀌었을 때(계정 전환·재로그인 — 같은 토큰을 다시 쓰더라도
    /// 사용자가 손을 쓴 직후라 바로 확인해야 한다) 부른다.
    /// </summary>
    public void ResetFailureBackoff()
    {
        _consecutiveFailures = 0;
        _failureRetryAt = DateTimeOffset.MinValue;
        _failedToken = null;
    }
}
