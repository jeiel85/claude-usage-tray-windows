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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.AuthTimeoutSeconds) };

    private readonly string _credentialsPath;
    private readonly FileSystemWatcher? _watcher;

    /// <summary>
    /// credentials.json 이 변경되면 발생 (계정 전환 감지용).
    /// 쓰기 완료 후 최소 500ms 지연을 두어 파일 잠금 방지.
    /// </summary>
    public event Action? CredentialsChanged;

    /// <param name="credentialsPath">자격 파일 경로. null 이면 ~/.claude/.credentials.json (테스트용 주입점).</param>
    public CredentialService(string? credentialsPath = null)
    {
        _credentialsPath = credentialsPath ?? DefaultCredentialsPath;

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
        if (!File.Exists(_credentialsPath)) return null;
        try
        {
            var json = File.ReadAllText(_credentialsPath);
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
    /// Returns the Claude subscription type from stored credentials (e.g. "free", "pro", "max").
    /// Returns null if credentials don't exist or the field is absent.
    /// </summary>
    public string? GetSubscriptionType() => Load()?.ClaudeAiOauth?.SubscriptionType;

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
                return NullIfBlank(oauth.AccessToken);

            // Try to refresh
            var refreshed = await TryRefreshAsync(oauth.RefreshToken);
            if (refreshed is null) return NullIfBlank(oauth.AccessToken); // fall back to existing token

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
