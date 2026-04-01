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

    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly FileSystemWatcher? _watcher;

    /// <summary>
    /// credentials.json 이 변경되면 발생 (계정 전환 감지용).
    /// 쓰기 완료 후 최소 500ms 지연을 두어 파일 잠금 방지.
    /// </summary>
    public event Action? CredentialsChanged;

    public CredentialService()
    {
        var dir = Path.GetDirectoryName(CredentialsPath)!;
        if (Directory.Exists(dir))
        {
            _watcher = new FileSystemWatcher(dir, ".credentials.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            // Changed 이벤트는 짧은 시간에 여러 번 올 수 있으므로 debounce
            _watcher.Changed += OnCredentialsFileChanged;
        }
    }

    private System.Timers.Timer? _debounceTimer;

    private void OnCredentialsFileChanged(object sender, FileSystemEventArgs e)
    {
        // 500ms debounce — 파일 저장 중 여러 이벤트 방지
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Timers.Timer(500) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) => CredentialsChanged?.Invoke();
        _debounceTimer.Start();
    }

    public ClaudeCredentials? Load()
    {
        if (!File.Exists(CredentialsPath)) return null;
        try
        {
            var json = File.ReadAllText(CredentialsPath);
            return JsonSerializer.Deserialize<ClaudeCredentials>(json);
        }
        catch { return null; }
    }

    public string? GetAccessToken()
    {
        var cred = Load();
        return cred?.ClaudeAiOauth?.AccessToken;
    }

    public string? GetOrganizationUuid() => Load()?.OrganizationUuid;

    public bool HasCredentials() => File.Exists(CredentialsPath);

    /// <summary>
    /// Returns a valid access token, refreshing it first if it has expired.
    /// Returns null if no credentials exist or refresh failed.
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
                return oauth.AccessToken;

            // Try to refresh
            var refreshed = await TryRefreshAsync(oauth.RefreshToken);
            if (refreshed is null) return oauth.AccessToken; // fall back to existing token

            // Persist updated credentials
            try
            {
                oauth.AccessToken = refreshed.AccessToken;
                oauth.ExpiresAt   = refreshed.ExpiresAt;
                if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                    oauth.RefreshToken = refreshed.RefreshToken;

                // Merge back — preserve other fields in the JSON
                var raw = JsonNode.Parse(File.ReadAllText(CredentialsPath))!;
                raw["claudeAiOauth"]!["accessToken"] = oauth.AccessToken;
                raw["claudeAiOauth"]!["expiresAt"]   = oauth.ExpiresAt;
                if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                    raw["claudeAiOauth"]!["refreshToken"] = oauth.RefreshToken;

                File.WriteAllText(CredentialsPath,
                    raw.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { /* ignore write errors */ }

            return oauth.AccessToken;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<RefreshResult?> TryRefreshAsync(string? refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken)) return null;
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

            var json  = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root  = doc.RootElement;

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

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private record RefreshResult(string AccessToken, long ExpiresAt, string? RefreshToken);
}
