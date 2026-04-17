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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.AuthTimeoutSeconds) };

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
        if (!File.Exists(CredentialsPath)) return null;
        try
        {
            var json = File.ReadAllText(CredentialsPath);
            return JsonSerializer.Deserialize<ClaudeCredentials>(json);
        }
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[CredentialService] Load failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return null;
        }
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

                // 자가 쓰기 플래그 설정 — FileSystemWatcher self-trigger 방지
                _isSelfWriting = true;
                try
                {
                    File.WriteAllText(CredentialsPath,
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

            return oauth.AccessToken;
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
        catch (Exception ex)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[CredentialService] Refresh failed: {ex.Message}");
#endif
            GC.KeepAlive(ex);
            return null;
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private record RefreshResult(string AccessToken, long ExpiresAt, string? RefreshToken);
}
