using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Antigravity (Google 의 Gemini Code Assist 데스크톱 IDE) 의 사용량 게이지를 가져오는 monitor.
///
/// 흐름:
///  1. Windows 자격 증명 관리자의 "gemini:antigravity" 항목에서 토큰을 읽는다.
///  2. 저장된 access_token 이 아직 유효하면 그대로 쓴다. 이 경우 OAuth client 자격 증명이 필요 없다.
///  3. 만료됐으면 refresh_token 으로 갱신한다. 이때만 client_id/secret 이 필요하며,
///     사용자가 적어 둔 파일이 없으면 설치된 language_server.exe 에서 찾아낸다.
///  4. v1internal:retrieveUserQuotaSummary 로 그룹별(Gemini / Claude·GPT) 주간·5시간 잔여량을 받는다.
///     Antigravity 앱의 "Models &amp; Usage" 화면이 그리는 값이 이것이다.
///  5. v1internal:loadCodeAssist 로 tier 표기를 함께 받는다.
///
/// Antigravity 를 실행하지 않아도 동작한다. 앱의 로컬 서버가 아니라 Google 백엔드를 직접 부르기 때문이다.
///
/// 모든 단계가 비공식 API 다. 클라이언트 업데이트로 엔드포인트나 헤더 규칙이 바뀌면 조용히 실패할 수 있으므로,
/// 실패는 예외 대신 <see cref="AntigravitySnapshot.ErrorMessage"/> 로 돌려보내 섹션만 비운다.
/// </summary>
public sealed class AntigravityUsageMonitor : IDisposable
{
    private const string CredentialTarget = "gemini:antigravity";

    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string CloudCodeBase = "https://daily-cloudcode-pa.googleapis.com/v1internal";

    /// <summary>
    /// 클라이언트 신원. 이 형식의 User-Agent 가 없으면 retrieveUserQuotaSummary 가 403 을 준다
    /// (retrieveUserQuota 는 없어도 통과하지만 화면과 다른 모델별 값이라 쓰지 않는다).
    /// 서버가 버전 값 자체를 검사하지는 않는다.
    /// </summary>
    private const string ClientUserAgent = "antigravity/2.6.0";

    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 사용자가 직접 적어 둔 OAuth client 파일. 형식: <c>{"client_id": "...", "client_secret": "..."}</c>.
    /// 이제는 필수가 아니며, 바이너리에서 찾아낸 값을 다음 실행에서 다시 찾지 않으려고 여기에 저장하기도 한다.
    /// </summary>
    private static readonly string ClientConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClaudeUsageTray", "antigravity-oauth-client.json");

    private readonly HttpClient _http;

    // refresh 로 받은 access_token 은 메모리에만 둔다 (자격 증명 관리자 쪽은 Antigravity 가 관리한다).
    private string? _accessToken;
    private DateTimeOffset _accessExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// 서버가 거부한 자격 증명 관리자 토큰. 폐기된 토큰은 기록된 만료 시각이 아직 남아 있어도 다시 쓰면 안 된다.
    /// 그대로 두면 만료 시각이 지날 때까지 매번 401 을 받으며 갱신 경로로 넘어가지 못한다.
    /// Antigravity 가 새 토큰을 저장하면 값이 달라지므로 저절로 풀린다.
    /// </summary>
    private string? _rejectedStoredToken;

    // 바이너리 스캔은 한 번만 시도한다 (144MB, 약 1.5초).
    private bool _clientLookupAttempted;

    public AntigravityUsageMonitor()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(AppConstants.AuthTimeoutSeconds) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(ClientUserAgent);
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }

    public async Task<AntigravitySnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        // 1. 자격 증명 관리자 — Antigravity 미설치/미로그인 환경에서는 조용히 패스한다.
        StoredCredential? cred;
        try
        {
            cred = ReadCredStore();
        }
        catch (Exception ex)
        {
            return Failure($"credstore read failed: {ex.Message}");
        }

        if (cred is null)
            return Informational("Antigravity not signed in");

        // 2. access_token 확보.
        string accessToken;
        try
        {
            var resolved = await ResolveAccessTokenAsync(cred, ct).ConfigureAwait(false);
            if (resolved is null)
            {
                // 저장된 토큰이 만료됐는데 갱신에 쓸 client 자격 증명을 찾지 못한 상태.
                // Antigravity 를 한 번 실행하면 앱이 토큰을 새로 발급해 두므로 오류로 취급하지 않는다.
                return Informational("Antigravity session expired");
            }
            accessToken = resolved;
        }
        catch (Exception ex)
        {
            return Failure($"token refresh failed: {ex.Message}");
        }

        // 3. 사용량 요약과 tier 를 함께 받는다.
        var summaryTask = CallV1InternalAsync("retrieveUserQuotaSummary", accessToken, ct);
        var tierTask = CallV1InternalAsync("loadCodeAssist", accessToken, ct);
        await Task.WhenAll(summaryTask, tierTask).ConfigureAwait(false);

        using var summaryDoc = summaryTask.Result;
        using var tierDoc = tierTask.Result;

        if (summaryDoc is null)
            return Failure("retrieveUserQuotaSummary returned no data");

        var buckets = ParseSummary(summaryDoc);
        if (buckets.Count == 0)
            return Failure("retrieveUserQuotaSummary returned no buckets");

        string? tierName = null, paidTierName = null;
        if (tierDoc is not null)
        {
            var root = tierDoc.RootElement;
            if (root.TryGetProperty("currentTier", out var current) && current.TryGetProperty("name", out var currentName))
                tierName = currentName.GetString();
            if (root.TryGetProperty("paidTier", out var paid) && paid.TryGetProperty("name", out var paidName))
                paidTierName = paidName.GetString();
        }

        return new AntigravitySnapshot
        {
            HasData = true,
            TierName = tierName,
            PaidTierName = paidTierName,
            Models = buckets,
        };
    }

    private static AntigravitySnapshot Informational(string message) =>
        new() { ErrorMessage = message, IsInformational = true };

    private static AntigravitySnapshot Failure(string message) =>
        new() { ErrorMessage = message };

    // ---------- 응답 파싱 ----------

    /// <summary>
    /// retrieveUserQuotaSummary 응답을 버킷 목록으로 편다.
    /// 구조: groups[] → buckets[]. 그룹 이름과 버킷 이름을 합쳐 표시 이름을 만든다
    /// ("Gemini Models" + "Weekly Limit" → "Gemini Models · Weekly Limit").
    /// </summary>
    internal static IReadOnlyList<AntigravityModelQuota> ParseSummary(JsonDocument doc)
    {
        var list = new List<AntigravityModelQuota>();

        var root = doc.RootElement;
        // 로컬 language server 를 거치면 한 겹 더 감싸여 오므로 양쪽을 모두 받아 준다.
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("response", out var wrapped) &&
            wrapped.ValueKind == JsonValueKind.Object)
        {
            root = wrapped;
        }

        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("groups", out var groups) ||
            groups.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var group in groups.EnumerateArray())
        {
            string groupName = group.TryGetProperty("displayName", out var gn) ? (gn.GetString() ?? "") : "";
            if (!group.TryGetProperty("buckets", out var buckets) || buckets.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var bucket in buckets.EnumerateArray())
            {
                string bucketId = bucket.TryGetProperty("bucketId", out var id) ? (id.GetString() ?? "") : "";
                string window = bucket.TryGetProperty("window", out var w) ? (w.GetString() ?? "") : "";
                string bucketName = bucket.TryGetProperty("displayName", out var bn) ? (bn.GetString() ?? "") : "";

                double remaining = 1.0;
                if (bucket.TryGetProperty("remainingFraction", out var frac) && frac.ValueKind == JsonValueKind.Number)
                    frac.TryGetDouble(out remaining);

                DateTimeOffset? reset = null;
                if (bucket.TryGetProperty("resetTime", out var rt) && rt.ValueKind == JsonValueKind.String &&
                    DateTimeOffset.TryParse(rt.GetString(), null,
                        System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                {
                    reset = parsed;
                }

                list.Add(new AntigravityModelQuota
                {
                    ModelId = bucketId,
                    TokenType = window,
                    RemainingFraction = Math.Clamp(remaining, 0.0, 1.0),
                    ResetTime = reset,
                    DisplayName = ComposeDisplayName(groupName, bucketName),
                });
            }
        }

        return list;
    }

    internal static string ComposeDisplayName(string groupName, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return bucketName.Trim();
        if (string.IsNullOrWhiteSpace(bucketName)) return groupName.Trim();
        return $"{groupName.Trim()} · {bucketName.Trim()}";
    }

    // ---------- HTTP ----------

    private async Task<JsonDocument?> CallV1InternalAsync(string method, string accessToken, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{CloudCodeBase}:{method}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        try
        {
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    // 다음 호출에서 다시 갱신하도록 캐시를 버리고,
                    // 방금 거부당한 토큰은 만료 시각이 남아 있어도 재사용하지 않는다.
                    _accessExpiresAt = DateTimeOffset.MinValue;
                    _rejectedStoredToken = accessToken;
                }
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

    // ---------- 토큰 ----------

    /// <summary>
    /// 쓸 수 있는 access_token 을 돌려준다. 갱신이 필요한데 client 자격 증명을 구하지 못하면 null.
    /// </summary>
    private async Task<string?> ResolveAccessTokenAsync(StoredCredential cred, CancellationToken ct)
    {
        if (IsCachedTokenUsable())
            return _accessToken;

        // Antigravity 가 직접 갱신해 둔 토큰이 아직 살아 있으면 그대로 쓴다. 가장 흔한 경로다.
        if (!string.IsNullOrEmpty(cred.AccessToken) &&
            !string.Equals(cred.AccessToken, _rejectedStoredToken, StringComparison.Ordinal) &&
            cred.ExpiresAt is { } expiry &&
            DateTimeOffset.UtcNow + TokenRefreshSkew < expiry)
        {
            return cred.AccessToken;
        }

        if (string.IsNullOrEmpty(cred.RefreshToken))
            return null;

        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (IsCachedTokenUsable())
                return _accessToken;

            foreach (var client in await LoadClientCandidatesAsync().ConfigureAwait(false))
            {
                var token = await TryRefreshAsync(client, cred.RefreshToken, ct).ConfigureAwait(false);
                if (token is null) continue;

                // 통한 조합만 남겨 두면 다음 실행에서 바이너리를 다시 훑지 않는다.
                TrySaveClientConfig(client);
                return token;
            }

            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsCachedTokenUsable() =>
        !string.IsNullOrEmpty(_accessToken) && DateTimeOffset.UtcNow + TokenRefreshSkew < _accessExpiresAt;

    /// <summary>사용자가 적어 둔 값을 먼저 쓰고, 없으면 설치된 바이너리에서 찾아낸 후보를 뒤에 붙인다.</summary>
    private async Task<IReadOnlyList<AntigravityOAuthClient>> LoadClientCandidatesAsync()
    {
        var candidates = new List<AntigravityOAuthClient>();

        var configured = ReadClientConfig();
        if (configured is not null) candidates.Add(configured);

        if (_clientLookupAttempted) return candidates;
        _clientLookupAttempted = true;

        var path = AntigravityOAuthClientLocator.FindLanguageServerPath();
        if (path is null) return candidates;

        try
        {
            // 144MB 를 순차로 훑는 작업이라 호출한 스레드에서 그대로 돌리면 안 된다.
            // 수동 새로고침은 UI 스레드에서 시작되므로 그동안 트레이 화면이 멈춘다.
            var scanned = await Task.Run(() =>
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return AntigravityOAuthClientLocator.ScanCandidates(fs);
            }).ConfigureAwait(false);

            // 이미 실패한 조합을 다시 시도하지 않는다.
            candidates.AddRange(scanned.Where(c => c != configured));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[AntigravityUsageMonitor] client lookup failed: {ex.Message}");
        }

        return candidates;
    }

    /// <summary>갱신에 성공하면 access_token, 자격 증명이 틀리면 null. 네트워크 오류는 예외로 올린다.</summary>
    private async Task<string?> TryRefreshAsync(AntigravityOAuthClient client, string refreshToken, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = client.ClientId,
            ["client_secret"] = client.ClientSecret,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);

        if (!resp.IsSuccessStatusCode)
        {
            // 400/401 은 "이 조합이 아니다" 라는 뜻이므로 다음 후보로 넘어간다.
            if (resp.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Unauthorized)
                return null;

            var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException($"OAuth refresh HTTP {(int)resp.StatusCode}: {Trim(body, 200)}");
        }

        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var token = doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(token)) return null;

        int expiresIn = doc.RootElement.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
            ? ei.GetInt32()
            : 3600;

        _accessToken = token;
        _accessExpiresAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(expiresIn);
        return token;
    }

    private static string Trim(string s, int n) => s.Length > n ? s[..n] + "…" : s;

    // ---------- OAuth client 파일 ----------

    private static AntigravityOAuthClient? ReadClientConfig()
    {
        try
        {
            if (!File.Exists(ClientConfigPath)) return null;

            using var fs = new FileStream(ClientConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var doc = JsonDocument.Parse(fs);
            var root = doc.RootElement;

            var id = root.TryGetProperty("client_id", out var a) ? a.GetString() : null;
            var secret = root.TryGetProperty("client_secret", out var b) ? b.GetString() : null;
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret)) return null;

            return new AntigravityOAuthClient(id, secret);
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveClientConfig(AntigravityOAuthClient client)
    {
        try
        {
            var dir = Path.GetDirectoryName(ClientConfigPath);
            if (string.IsNullOrEmpty(dir)) return;
            Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["client_id"] = client.ClientId,
                ["client_secret"] = client.ClientSecret,
            }, new JsonSerializerOptions { WriteIndented = true });

            // 쓰다 만 파일이 남지 않도록 임시 파일에 쓴 뒤 옮긴다.
            // 임시 경로가 고정이라 다른 인스턴스가 같은 파일을 보고 있을 수 있으므로 공유 모드로 연다
            // (여기서 IOException 이 나면 캐시를 못 남겨 다음 실행에서 바이너리를 또 훑게 된다).
            var temp = ClientConfigPath + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                writer.Write(json);
            }
            File.Move(temp, ClientConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"[AntigravityUsageMonitor] client config save failed: {ex.Message}");
        }
    }

    // ---------- Windows 자격 증명 관리자 ----------

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

    internal static StoredCredential? ReadCredStore()
    {
        if (!CredRead(CredentialTarget, CRED_TYPE_GENERIC, 0, out IntPtr ptr))
            return null;          // ERROR_NOT_FOUND 등 — Antigravity 미설치/미로그인

        try
        {
            var c = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (c.CredentialBlobSize == 0 || c.CredentialBlob == IntPtr.Zero) return null;

            var buf = new byte[c.CredentialBlobSize];
            Marshal.Copy(c.CredentialBlob, buf, 0, buf.Length);
            return ParseCredentialBlob(Encoding.UTF8.GetString(buf).TrimEnd('\0'));
        }
        finally
        {
            CredFree(ptr);
        }
    }

    /// <summary>
    /// 자격 증명 blob 은 <c>{"token":{"access_token":…,"refresh_token":…,"expiry":…},"id_token":…}</c> 형태다.
    /// id_token 은 계정 신원이라 읽지 않는다.
    /// </summary>
    internal static StoredCredential? ParseCredentialBlob(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("token", out var token) || token.ValueKind != JsonValueKind.Object)
            return null;

        var access = token.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var refresh = token.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;

        DateTimeOffset? expiry = null;
        if (token.TryGetProperty("expiry", out var e) && e.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(e.GetString(), null,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            expiry = parsed;
        }

        return new StoredCredential(access, refresh, expiry);
    }
}

/// <summary>자격 증명 관리자에 Antigravity 가 저장해 둔 토큰 묶음.</summary>
internal sealed record StoredCredential(string? AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
