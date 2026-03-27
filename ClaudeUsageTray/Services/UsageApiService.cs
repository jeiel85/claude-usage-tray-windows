using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class UsageApiService
{
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private readonly HttpClient _http;
    private readonly CredentialService _credentials;

    public string? LastRawResponse { get; private set; }
    public string? LastError { get; private set; }
    public int LastRetryAfterSeconds { get; private set; } = 0;

    public UsageApiService(CredentialService credentials)
    {
        _credentials = credentials;
        _http = new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task<UsageResponse?> FetchUsageAsync()
    {
        var token = await _credentials.GetValidAccessTokenAsync();
        if (token is null)
        {
            LastError = "No access token found";
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
                if ((int)response.StatusCode == 429 &&
                    response.Headers.TryGetValues("Retry-After", out var vals) &&
                    int.TryParse(vals.FirstOrDefault(), out var ra))
                {
                    LastRetryAfterSeconds = ra;
                }
                LastError = $"HTTP {(int)response.StatusCode}: {LastRawResponse}";
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
