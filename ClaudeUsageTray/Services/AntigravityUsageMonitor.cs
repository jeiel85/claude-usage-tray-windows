using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Antigravity (Google's Gemini Code Assist desktop IDE, fork of Codeium/Windsurf) 의
/// 모델별 quota 데이터를 가져오는 monitor.
///
/// 흐름:
///  1. Windows Credential Manager 의 "gemini:antigravity" 항목에서 refresh_token 읽기.
///  2. oauth2.googleapis.com 에 client_id/secret 으로 refresh → 새 access_token.
///  3. daily-cloudcode-pa.googleapis.com 의 v1internal:retrieveUserQuota 호출 → 모델별 quota.
///  4. v1internal:loadCodeAssist 도 호출해 tier 정보 함께 노출.
///
/// 모든 단계는 비공식 API. Antigravity 마이너 업데이트로 client_id/secret 가 바뀔 수 있음.
/// 그 경우 사용자가 새 PC dump 떠서 secret 만 교체하면 다시 작동.
/// </summary>
public sealed class AntigravityUsageMonitor : IDisposable
{
    private const string CredentialTarget = "gemini:antigravity";

    private const string TokenEndpoint    = "https://oauth2.googleapis.com/token";
    private const string CloudCodeBase    = "https://daily-cloudcode-pa.googleapis.com/v1internal";

    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 사용자 PC 의 OAuth client config 파일 경로.
    /// 형식: <c>{"client_id": "...", "client_secret": "..."}</c>.
    /// 파일이 없으면 monitor 가 즉시 setup-needed 상태로 폴백 (UI 섹션 숨김).
    /// 값 추출 방법은 README 의 "Antigravity 통합" 섹션 참조.
    /// </summary>
    private static readonly string ClientConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTray", "antigravity-oauth-client.json");

    private readonly HttpClient _http;

    // In-memory cached access_token (avoid hitting Google's token endpoint on every poll).
    private string? _accessToken;
    private DateTimeOffset _accessExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AntigravityUsageMonitor()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Antigravity/2.0.6 (windows x64)");
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }

    public async Task<AntigravitySnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        // 0. OAuth client 설정 — 없으면 setup-needed.
        OAuthClientConfig? client;
        try
        {
            client = ReadClientConfig();
        }
        catch (Exception ex)
        {
            return new AntigravitySnapshot { ErrorMessage = $"client config read failed: {ex.Message}" };
        }
        if (client is null)
            return new AntigravitySnapshot { ErrorMessage = "Antigravity OAuth client not configured" };

        // 1. credstore 가 있는지부터 — Antigravity 미설치/미로그인 환경에서는 조용히 패스.
        string? refreshToken;
        string? userEmail = null;
        try
        {
            var cred = ReadCredStore();
            if (cred is null)
                return new AntigravitySnapshot { ErrorMessage = "Antigravity not signed in" };
            refreshToken = cred.RefreshToken;
        }
        catch (Exception ex)
        {
            return new AntigravitySnapshot { ErrorMessage = $"credstore read failed: {ex.Message}" };
        }

        if (string.IsNullOrEmpty(refreshToken))
            return new AntigravitySnapshot { ErrorMessage = "no refresh_token in credstore" };

        // 2. access_token 확보 (캐시 우선).
        string accessToken;
        try
        {
            accessToken = await EnsureAccessTokenAsync(client, refreshToken, ct);
        }
        catch (Exception ex)
        {
            return new AntigravitySnapshot { ErrorMessage = $"token refresh failed: {ex.Message}" };
        }

        // 3. 두 endpoint 병렬 호출.
        Task<JsonDocument?> quotaTask = CallV1InternalAsync("retrieveUserQuota", accessToken, ct);
        Task<JsonDocument?> tierTask  = CallV1InternalAsync("loadCodeAssist", accessToken, ct);

        await Task.WhenAll(quotaTask, tierTask).ConfigureAwait(false);

        using var quotaDoc = quotaTask.Result;
        using var tierDoc  = tierTask.Result;

        if (quotaDoc is null)
            return new AntigravitySnapshot { ErrorMessage = "retrieveUserQuota returned no data" };

        var models = ParseBuckets(quotaDoc);
        string? tierName = null, paidTierName = null;
        if (tierDoc is not null)
        {
            var root = tierDoc.RootElement;
            if (root.TryGetProperty("currentTier", out var ct1) && ct1.TryGetProperty("name", out var n1))
                tierName = n1.GetString();
            if (root.TryGetProperty("paidTier", out var pt) && pt.TryGetProperty("name", out var n2))
                paidTierName = n2.GetString();
        }

        return new AntigravitySnapshot
        {
            HasData = true,
            TierName = tierName,
            PaidTierName = paidTierName,
            UserEmail = userEmail,
            Models = models,
        };
    }

    // ---------- internals ----------

    private static IReadOnlyList<AntigravityModelQuota> ParseBuckets(JsonDocument doc)
    {
        var list = new List<AntigravityModelQuota>();
        if (!doc.RootElement.TryGetProperty("buckets", out var buckets) ||
            buckets.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var b in buckets.EnumerateArray())
        {
            string modelId = b.TryGetProperty("modelId", out var m) ? (m.GetString() ?? "") : "";
            string tokenType = b.TryGetProperty("tokenType", out var t) ? (t.GetString() ?? "") : "";
            double frac = 1.0;
            if (b.TryGetProperty("remainingFraction", out var r))
            {
                if (r.ValueKind == JsonValueKind.Number) r.TryGetDouble(out frac);
            }
            DateTimeOffset? reset = null;
            if (b.TryGetProperty("resetTime", out var rt) && rt.ValueKind == JsonValueKind.String)
            {
                if (DateTimeOffset.TryParse(rt.GetString(), null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                    reset = parsed;
            }
            list.Add(new AntigravityModelQuota
            {
                ModelId = modelId,
                TokenType = tokenType,
                RemainingFraction = Math.Clamp(frac, 0.0, 1.0),
                ResetTime = reset,
            });
        }
        return list;
    }

    private async Task<JsonDocument?> CallV1InternalAsync(string method, string accessToken, CancellationToken ct)
    {
        var url = $"{CloudCodeBase}:{method}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                // 401 → 캐시 토큰 무효화 (다음 호출에서 refresh).
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    _accessExpiresAt = DateTimeOffset.MinValue;
                return null;
            }
            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> EnsureAccessTokenAsync(OAuthClientConfig client, string refreshToken, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessToken) &&
            DateTimeOffset.UtcNow + TokenRefreshSkew < _accessExpiresAt)
        {
            return _accessToken!;
        }

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // double-check after taking the lock
            if (!string.IsNullOrEmpty(_accessToken) &&
                DateTimeOffset.UtcNow + TokenRefreshSkew < _accessExpiresAt)
            {
                return _accessToken!;
            }

            var form = new Dictionary<string, string>
            {
                ["client_id"]     = client.ClientId,
                ["client_secret"] = client.ClientSecret,
                ["grant_type"]    = "refresh_token",
                ["refresh_token"] = refreshToken,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(form),
            };
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new Exception($"OAuth refresh HTTP {(int)resp.StatusCode}: {Trim(body, 200)}");
            }
            using var doc = await JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            var at = root.GetProperty("access_token").GetString();
            int expiresIn = root.TryGetProperty("expires_in", out var ei) ? ei.GetInt32() : 3600;
            if (string.IsNullOrEmpty(at)) throw new Exception("empty access_token in OAuth response");

            _accessToken = at;
            _accessExpiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(expiresIn);
            return at;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private static string Trim(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    // ---------- Windows Credential Manager P/Invoke ----------

    private sealed record CredentialPayload(string? AccessToken, string? RefreshToken);
    private sealed record OAuthClientConfig(string ClientId, string ClientSecret);

    private static OAuthClientConfig? ReadClientConfig()
    {
        if (!File.Exists(ClientConfigPath)) return null;
        var json = File.ReadAllText(ClientConfigPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id  = root.TryGetProperty("client_id",     out var a) ? a.GetString() : null;
        var sec = root.TryGetProperty("client_secret", out var b) ? b.GetString() : null;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(sec)) return null;
        return new OAuthClientConfig(id, sec);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr cred);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);

    private const uint CRED_TYPE_GENERIC = 1;

    private static CredentialPayload? ReadCredStore()
    {
        IntPtr ptr;
        if (!CredRead(CredentialTarget, CRED_TYPE_GENERIC, 0, out ptr))
            return null;          // ERROR_NOT_FOUND 등 — Antigravity 미설치/미로그인
        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return null;
            var buf = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, buf, 0, buf.Length);
            var json = Encoding.UTF8.GetString(buf).TrimEnd('\0');
            using var doc = JsonDocument.Parse(json);
            var token = doc.RootElement.GetProperty("token");
            return new CredentialPayload(
                token.TryGetProperty("access_token", out var a) ? a.GetString() : null,
                token.TryGetProperty("refresh_token", out var r) ? r.GetString() : null);
        }
        finally
        {
            CredFree(ptr);
        }
    }
}
