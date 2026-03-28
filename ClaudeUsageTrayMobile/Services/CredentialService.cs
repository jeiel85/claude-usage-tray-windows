using System.Net.Http;
using System.Text;
using System.Text.Json;
using ClaudeUsageTrayMobile.Models;

namespace ClaudeUsageTrayMobile.Services;

/// <summary>
/// Manages OAuth credentials using SecureStorage.
/// On mobile, the user provides a refresh token (copied from Claude Code credentials,
/// or obtained via the OAuth PKCE setup flow). Tokens are then refreshed automatically.
/// </summary>
public class CredentialService
{
    private const string KeyAccessToken  = "claude_access_token";
    private const string KeyRefreshToken = "claude_refresh_token";
    private const string KeyExpiresAt    = "claude_expires_at";

    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<bool> HasCredentialsAsync()
    {
        var token = await SecureStorage.GetAsync(KeyRefreshToken);
        return !string.IsNullOrEmpty(token);
    }

    public async Task SaveCredentialsAsync(string accessToken, string refreshToken, long expiresAt)
    {
        await SecureStorage.SetAsync(KeyAccessToken, accessToken);
        await SecureStorage.SetAsync(KeyRefreshToken, refreshToken);
        await SecureStorage.SetAsync(KeyExpiresAt, expiresAt.ToString());
    }

    public void ClearCredentials()
    {
        SecureStorage.Remove(KeyAccessToken);
        SecureStorage.Remove(KeyRefreshToken);
        SecureStorage.Remove(KeyExpiresAt);
    }

    /// <summary>
    /// Returns a valid access token, refreshing if expired. Returns null if no credentials.
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync()
    {
        var accessToken  = await SecureStorage.GetAsync(KeyAccessToken);
        var refreshToken = await SecureStorage.GetAsync(KeyRefreshToken);
        var expiresAtStr = await SecureStorage.GetAsync(KeyExpiresAt);

        if (string.IsNullOrEmpty(refreshToken)) return null;

        long expiresAt = long.TryParse(expiresAtStr, out var ea) ? ea : 0;
        bool isExpired = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() >= expiresAt - 60_000;

        if (!isExpired && !string.IsNullOrEmpty(accessToken))
            return accessToken;

        // Refresh
        var result = await TryRefreshAsync(refreshToken);
        if (result is null) return accessToken; // Fall back to stale token

        await SaveCredentialsAsync(result.AccessToken, result.NewRefreshToken ?? refreshToken, result.ExpiresAt);
        return result.AccessToken;
    }

    private static async Task<RefreshResult?> TryRefreshAsync(string refreshToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                grant_type    = "refresh_token",
                refresh_token = refreshToken,
                client_id     = ClientId
            });
            var response = await Http.SendAsync(new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("access_token", out var at)) return null;

            long expiresAt;
            if (root.TryGetProperty("expires_in", out var expiresIn))
                expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + expiresIn.GetInt64() * 1000;
            else
                expiresAt = DateTimeOffset.UtcNow.AddHours(5).ToUnixTimeMilliseconds();

            string? newRefresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;

            return new RefreshResult(at.GetString()!, expiresAt, newRefresh);
        }
        catch { return null; }
    }

    private record RefreshResult(string AccessToken, long ExpiresAt, string? NewRefreshToken);
}
