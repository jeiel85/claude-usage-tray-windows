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
///     Antigravity 앱의 "Models &amp; Usage" 화면이 그리는 값이 이것이다. 공유 풀과 별개로 모델 하나에만 걸린
///     한도(Gemini 4 Argon 주간, #204)도 자기 그룹으로 함께 온다.
///  5. v1internal:loadCodeAssist 로 tier 표기와 Google AI 크레딧 잔액을 함께 받는다.
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
    /// 클라이언트 신원. "antigravity/" 로 시작하는 User-Agent 가 없으면 retrieveUserQuotaSummary 가 403 을 준다
    /// (retrieveUserQuota 는 없어도 통과하지만 화면과 다른 모델별 값이라 쓰지 않는다).
    ///
    /// 서버는 이 값의 클라이언트 계열과 버전을 보고 내려줄 버킷을 고른다. Gemini 4 Argon 주간 한도(#204)는
    /// 데스크톱 앱(hub) 계열의 최신 식별자에만 오고, IDE 계열("antigravity/ide/…")이나 예전 형식
    /// (<see cref="LegacyClientUserAgent"/>)으로 물으면 빠진다. 그래서 데스크톱 앱이 실제로 보내는
    /// "antigravity/hub/버전 os/arch" 형식을 쓴다. 버전은 Argon 이 나온(2026-10-13) 뒤의 데스크톱 앱 버전이다.
    /// 이 형식은 '/' 가 두 번 들어가 User-Agent 문법 검사를 통과하지 못하므로 검사 없이 넣는다.
    /// </summary>
    internal static readonly string ClientUserAgent = $"antigravity/hub/2.23.0 windows/{UserAgentArch()}";

    /// <summary>
    /// 이전까지 쓰던 식별자. 공유 풀(Gemini · Claude/GPT) 게이지는 이것으로도 온다.
    /// 서버가 <see cref="ClientUserAgent"/> 를 거부(403)하면 이것으로 한 번 더 물어 게이지가 통째로 사라지지 않게 한다.
    /// </summary>
    internal const string LegacyClientUserAgent = "antigravity/2.6.0";

    private static string UserAgentArch() =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";

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

    public AntigravityUsageMonitor() : this(new HttpClientHandler())
    {
    }

    /// <summary>테스트용 — 서버 대신 꾸민 응답을 돌려주는 handler 를 넣는다.</summary>
    internal AntigravityUsageMonitor(HttpMessageHandler handler)
    {
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(AppConstants.AuthTimeoutSeconds) };
        // 토큰 갱신 요청에 붙는 기본값. v1internal 호출은 요청마다 식별자를 직접 고른다(CallV1InternalAsync).
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", ClientUserAgent);
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
            return Failure("retrieveUserQuotaSummary returned no usable buckets");

        var tier = tierDoc is null ? CodeAssistTier.Empty : ParseCodeAssistTier(tierDoc);

        return new AntigravitySnapshot
        {
            HasData = true,
            TierName = tier.TierName,
            PaidTierName = tier.PaidTierName,
            AiCredits = tier.AiCredits,
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
    /// 응답 최상위에도 그룹에 속하지 않는 buckets[] 자리가 있다(proto 의 RetrieveUserQuotaSummaryResponse.buckets).
    /// 실제 응답에서 채워진 사례는 없고, 같은 버킷을 그룹 없이 다시 실은 것일 수 있어 그룹에서 아무것도 못 얻었을 때만 쓴다
    /// (이 자리를 읽는 다른 클라이언트들도 같은 규칙이다).
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

        if (root.ValueKind != JsonValueKind.Object)
            return list;

        if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                if (group.ValueKind != JsonValueKind.Object) continue;
                string groupName = ReadString(group, "displayName");
                if (group.TryGetProperty("buckets", out var buckets) && buckets.ValueKind == JsonValueKind.Array)
                    AddBuckets(list, buckets, groupName);
            }
        }

        if (list.Count == 0 &&
            root.TryGetProperty("buckets", out var ungrouped) && ungrouped.ValueKind == JsonValueKind.Array)
        {
            AddBuckets(list, ungrouped, "");
        }

        return list;
    }

    private static void AddBuckets(List<AntigravityModelQuota> list, JsonElement buckets, string groupName)
    {
        foreach (var bucket in buckets.EnumerateArray())
        {
            if (bucket.ValueKind != JsonValueKind.Object) continue;

            var quota = ParseBucket(bucket, groupName);
            if (quota is not null) list.Add(quota);
        }
    }

    private static AntigravityModelQuota? ParseBucket(JsonElement bucket, string groupName)
    {
        // disabled 는 이 tier 가 계량하지 않는 창이다. 이런 버킷도 remainingFraction 1 을 달고 와서 그대로 그리면
        // "0% 사용" 게이지가 된다 — 주간 한도를 다 써 막힌 순간(gemini-weekly 0, gemini-5h disabled)에도 그렇다.
        if (bucket.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
            return null;

        // 잔여량은 remainingFraction 과 remainingAmount 중 하나만 온다(proto 의 oneof remaining).
        // 수량형(remainingAmount)은 한도를 모르니 사용률로 바꿀 수 없고, 비율이 없을 때 "다 남았다" 로 두면
        // 소진된 버킷이 0% 로 그려진다. 사용률을 지어내지 않고 행을 만들지 않는다 —
        // gemini-cli 도 retrieveUserQuota 에서 remainingFraction 이 없는 버킷은 건너뛴다.
        if (!bucket.TryGetProperty("remainingFraction", out var frac) ||
            frac.ValueKind != JsonValueKind.Number ||
            !frac.TryGetDouble(out var remaining))
        {
            return null;
        }

        DateTimeOffset? reset = null;
        if (bucket.TryGetProperty("resetTime", out var rt) && rt.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(rt.GetString(), null,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            reset = parsed;
        }

        return new AntigravityModelQuota
        {
            ModelId = ReadString(bucket, "bucketId"),
            TokenType = ReadString(bucket, "window"),
            RemainingFraction = Math.Clamp(remaining, 0.0, 1.0),
            ResetTime = reset,
            DisplayName = ComposeDisplayName(groupName, ReadString(bucket, "displayName")),
            GroupName = groupName.Trim(),
        };
    }

    private static string ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>
    /// loadCodeAssist 응답에서 tier 이름과 Google AI 크레딧 잔액을 읽는다.
    /// tier 가 객체가 아니어도 예외 없이 빈 값으로 넘어간다 — 배지·크레딧은 부가 정보라 게이지를 막으면 안 된다.
    /// </summary>
    internal static CodeAssistTier ParseCodeAssistTier(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            return CodeAssistTier.Empty;

        root.TryGetProperty("currentTier", out var current);
        root.TryGetProperty("paidTier", out var paid);

        return new CodeAssistTier(
            TierName: ReadTierName(current),
            PaidTierName: ReadTierName(paid),
            AiCredits: ParseAiCredits(paid));
    }

    private static string? ReadTierName(JsonElement tier) =>
        tier.ValueKind == JsonValueKind.Object &&
        tier.TryGetProperty("name", out var name) &&
        name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;

    /// <summary>
    /// <c>paidTier.availableCredits[]</c> 에서 GOOGLE_ONE_AI 항목의 <c>creditAmount</c> 를 더한다.
    /// gemini-cli 의 <c>getG1CreditBalance</c> 와 같은 규칙이다 — 항목이 없으면 null(크레딧 대상 아님),
    /// 있으면 0 이어도 값으로 돌려준다. creditAmount 는 int64 라 JSON 에서 문자열로 오고, 못 읽으면 0 으로 센다.
    /// </summary>
    internal static long? ParseAiCredits(JsonElement tier)
    {
        if (tier.ValueKind != JsonValueKind.Object ||
            !tier.TryGetProperty("availableCredits", out var credits) ||
            credits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        long? total = null;
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.ValueKind != JsonValueKind.Object ||
                ReadString(credit, "creditType") != GoogleOneAiCreditType)
            {
                continue;
            }

            total = (total ?? 0) + ReadInt64(credit, "creditAmount");
        }
        return total;
    }

    private const string GoogleOneAiCreditType = "GOOGLE_ONE_AI";

    private static long ReadInt64(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value)) return 0;
        return value.ValueKind switch
        {
            JsonValueKind.String when long.TryParse(value.GetString(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt64(out var number) => number,
            _ => 0,
        };
    }

    internal static string ComposeDisplayName(string groupName, string bucketName)
    {
        var group = groupName.Trim();
        var bucket = bucketName.Trim();
        if (group.Length == 0) return bucket;
        if (bucket.Length == 0) return group;
        // 모델 한정 그룹은 버킷 이름에 그룹 이름이 이미 들어 있다 — 다시 붙이면 같은 말이 두 번 나온다
        // ("Gemini 4 Argon" + "Gemini 4 Argon Weekly Limit").
        if (bucket.StartsWith(group, StringComparison.OrdinalIgnoreCase)) return bucket;
        return $"{group} · {bucket}";
    }

    // ---------- HTTP ----------

    /// <summary>
    /// v1internal 메서드를 부른다. 서버가 지금 식별자를 거부(403)하면 예전 식별자로 한 번 더 묻는다 —
    /// 식별자 규칙이 바뀌어도 공유 풀 게이지까지 잃지는 않게 하려는 것이다. 401 은 토큰 문제라 다시 묻지 않는다.
    /// 끝내 받지 못하면 null.
    /// </summary>
    internal async Task<JsonDocument?> CallV1InternalAsync(string method, string accessToken, CancellationToken ct)
    {
        var (doc, status) = await SendV1InternalAsync(method, accessToken, ClientUserAgent, ct).ConfigureAwait(false);
        if (status != System.Net.HttpStatusCode.Forbidden)
            return doc;

        (doc, _) = await SendV1InternalAsync(method, accessToken, LegacyClientUserAgent, ct).ConfigureAwait(false);
        return doc;
    }

    /// <summary>요청 한 번. 응답을 받지 못했으면(네트워크 오류 등) 상태 코드도 null 이다.</summary>
    private async Task<(JsonDocument? Doc, System.Net.HttpStatusCode? Status)> SendV1InternalAsync(
        string method, string accessToken, string userAgent, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{CloudCodeBase}:{method}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
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
                return (null, resp.StatusCode);
            }

            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            return (await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false), resp.StatusCode);
        }
        catch
        {
            return (null, null);
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

/// <summary>loadCodeAssist 응답에서 쓰는 값 — tier 이름과 Google AI 크레딧 잔액.</summary>
internal sealed record CodeAssistTier(string? TierName, string? PaidTierName, long? AiCredits)
{
    public static CodeAssistTier Empty { get; } = new(null, null, null);
}
