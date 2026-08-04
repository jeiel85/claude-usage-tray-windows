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
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.ApiTimeoutSeconds) };
    private readonly CredentialService _credentials;

    public string? LastRawResponse { get; private set; }
    public string? LastError { get; private set; }
    public int LastRetryAfterSeconds { get; private set; } = 0;

    public UsageApiService(CredentialService credentials)
    {
        _credentials = credentials;
    }

    public async Task<UsageResponse?> FetchUsageAsync()
    {
        var token = await _credentials.GetValidAccessTokenAsync();
        // 빈 문자열도 "토큰 없음"이다 — 그대로 보내면 인증 없는 요청이 되어 429 로 돌아오고,
        // 호출부가 이를 일시적 rate limit 으로 오진한다.
        if (string.IsNullOrWhiteSpace(token))
        {
            LastError = NoTokenError;
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
                LastError = $"HTTP {statusCode}: {LastRawResponse}";
                return null;
            }

            LastError = null;
            return JsonSerializer.Deserialize<UsageResponse>(LastRawResponse,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return null;
        }
    }
}
