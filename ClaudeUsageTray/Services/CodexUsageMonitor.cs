using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class CodexUsageMonitor
{
    private static readonly string SessionsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "sessions");

    private static readonly string AuthPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "auth.json");

    // 실제 codex CLI(openai/codex 저장소 backend-client/src/client/rate_limit_resets.rs)가 부르는
    // 계정 단위 사용량 엔드포인트. 예전 값("api.openai-v2.com/backend-api/codex/usage")은 DNS 에
    // 존재조차 하지 않는 도메인이었다 — 이 경로는 한 번도 실제로 성공한 적이 없었다는 뜻이다
    // (#148 은 auth.json 경로만 지적했지만, 도메인과 응답 스키마 자체가 잘못돼 있던 건 이번에
    // 실제 계정으로 실측하며 드러났다. 아래 응답 파싱도 그때 확인한 실제 스키마 기준이다).
    private const string UsageApiEndpoint = "https://chatgpt.com/backend-api/wham/usage";

    /// <summary>Direct API 로 읽은 스냅샷의 <see cref="ProviderUsageSnapshot.DataSource"/> 값.</summary>
    public const string DirectApiSource = "Direct API";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.ApiTimeoutSeconds) };

    public Task<ProviderUsageSnapshot> GetTodaySnapshotAsync(bool useDirectApi = true) =>
        GetTodaySnapshotAsync(useDirectApi, _http, AuthPath, SessionsPath);

    // HTTP·경로를 주입받는 오버로드 — 단위 테스트에서 가짜 응답과 임시 로그인 파일·로그 폴더를 쓰기 위한 것.
    internal async Task<ProviderUsageSnapshot> GetTodaySnapshotAsync(
        bool useDirectApi, HttpClient http, string authPath, string sessionsPath)
    {
        var apiStatus = CodexApiStatus.NotAttempted;
        if (useDirectApi)
        {
            var (apiSnapshot, status) = await FetchUsageFromApiAsync(http, authPath);
            apiStatus = status;
            if (apiSnapshot is { HasData: true })
            {
                // API 데이터가 있으면 로그 기반 데이터와 병합하거나 API 우선 사용
                // 여기서는 API 데이터를 우선하고, 상세 히스토리(시간대별)만 로그에서 보완
                var logSnapshot = GetTodaySnapshot(sessionsPath);
                apiSnapshot.HourlyTokens = logSnapshot.HourlyTokens;
                apiSnapshot.TotalInputTokens = logSnapshot.TotalInputTokens;
                apiSnapshot.TotalOutputTokens = logSnapshot.TotalOutputTokens;
                apiSnapshot.TotalCacheReadTokens = logSnapshot.TotalCacheReadTokens;
                apiSnapshot.SessionCount = logSnapshot.SessionCount;
                apiSnapshot.CodexApiStatus = status;
                return apiSnapshot;
            }
        }

        // 폴백 이유를 함께 싣는다 — 로그 값은 이 PC 에서 Codex 를 써야 생겨 옛 값·빈 값일 수 있고, 모델별 추가 한도
        // (Spark 등)는 사실상 API 응답에서만 온다. 로그인이 만료된 PC 에서 화면이 왜 그런지 설명할 수 있어야 한다(#198).
        var snapshot = GetTodaySnapshot(sessionsPath);
        snapshot.DataSource = "Local Log";
        snapshot.PlanType ??= TryReadPlanTypeFromAuth(authPath);
        snapshot.CodexApiStatus = apiStatus;
        return snapshot;
    }

    /// <summary>
    /// <b>지금</b> 로그인돼 있는 계정의 요금제. 로그아웃하면(파일이 없거나 id_token 이 사라지면) null 이다.
    /// 스냅샷의 <see cref="ProviderUsageSnapshot.PlanType"/> 은 날짜와 무관하게 가장 최근 세션 로그의
    /// <c>rate_limits.plan_type</c> 을 집어오므로 로그아웃 뒤에도 남는다 — "지금 구독 중인가" 를 물을 때는
    /// 반드시 이쪽을 쓴다(과거 값으로 판단하면 해지·로그아웃 후에도 영영 구독 중으로 보인다).
    /// </summary>
    public static string? GetCurrentPlanType() => TryReadPlanTypeFromAuth(AuthPath);

    /// <summary>
    /// <b>지금</b> 로그인돼 있는 Codex 계정의 식별자 원문. 로그아웃이거나 읽지 못하면 null.
    /// 여러 PC 가 같은 ntfy 토픽을 쓸 때 서로 다른 계정의 알림이 같은 사건으로 오인되지 않도록 알림 키에 섞는다(#175).
    /// </summary>
    public static string? GetCurrentAccountIdentity() => TryReadAccountIdentity(AuthPath);

    /// <summary>
    /// 로그인 파일에서 계정 식별자를 만든다.
    ///
    /// Input : <c>~/.codex/auth.json</c> 경로
    /// Output: <c>"{account_id}|{user_id}"</c> (한쪽만 알면 다른 쪽은 빈 문자열). 둘 다 모르면 null
    /// 핵심 로직: 같은 계정이면 어느 PC 에서든 같은 값이어야 한다 — 그래서 토큰(갱신 때마다 바뀜)이 아니라
    ///           계정·사용자 ID 만 쓴다. <c>tokens.account_id</c> 는 #148 에서 실제 파일로 확인된 자리이고,
    ///           id_token 의 <c>chatgpt_user_id</c>(없으면 <c>user_id</c>)·<c>chatgpt_account_id</c> 는 Codex CLI 소스
    ///           (openai/codex codex-rs/login/src/token_data.rs)가 읽는 클레임 이름이다. 워크스페이스(계정) ID 만으로는
    ///           같은 워크스페이스의 다른 사용자가 구분되지 않아 사용자 ID 도 함께 쓴다.
    ///           id_token 은 서명 검증 없이 payload 만 읽는다 — 표시·식별 전용이며 인증에 쓰지 않는다.
    /// </summary>
    internal static string? TryReadAccountIdentity(string authPath)
    {
        try
        {
            if (!File.Exists(authPath)) return null;

            using var doc = JsonDocument.Parse(ReadAuthFile(authPath));
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
                return null;

            var accountId = ReadString(tokens, "account_id");
            string? userId = null;

            if (ReadString(tokens, "id_token") is { } idToken)
            {
                using var payload = DecodeJwtPayload(idToken);
                if (payload is not null &&
                    payload.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                    auth.ValueKind == JsonValueKind.Object)
                {
                    userId = ReadString(auth, "chatgpt_user_id") ?? ReadString(auth, "user_id");
                    accountId ??= ReadString(auth, "chatgpt_account_id");
                }
            }

            return accountId is null && userId is null ? null : $"{accountId}|{userId}";
        }
        catch
        {
            return null;
        }
    }

    // auth.json 은 Codex 가 토큰을 갱신할 때 다시 쓰고 로그아웃할 때 지운다. File.ReadAllText(FileShare.Read)로 열면
    // Codex 가 쓰기로 열어 둔 동안엔 읽지 못하고, 읽는 동안엔 Codex 의 쓰기·삭제를 막는다 — 쓰기·삭제 공유를 허용해 연다(#201).
    // 앱은 이 파일에 쓰지 않는다.
    private static string ReadAuthFile(string authPath)
    {
        using var stream = new FileStream(authPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>
    /// 로그인 파일(<c>~/.codex/auth.json</c>)의 id_token 에서 요금제를 읽는다.
    /// 세션 로그의 <c>rate_limits.plan_type</c> 은 오늘 Codex 를 한 번이라도 써야 생기므로,
    /// 요금제 배지가 "쓰기 전에는 안 보이는" 값이 되지 않도록 여기서 보완한다.
    ///
    /// id_token 은 서명 검증 없이 payload 만 읽는다. 이 값은 <b>표시 전용</b>이며 인증이나 권한
    /// 판단에 쓰지 않는다(파일 자체가 이미 사용자 소유의 로컬 자격 파일이다).
    /// </summary>
    internal static string? TryReadPlanTypeFromAuth(string authPath)
    {
        try
        {
            if (!File.Exists(authPath)) return null;

            using var doc = JsonDocument.Parse(ReadAuthFile(authPath));
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens) ||
                tokens.ValueKind != JsonValueKind.Object ||
                !tokens.TryGetProperty("id_token", out var idTokenEl) ||
                idTokenEl.ValueKind != JsonValueKind.String)
                return null;

            using var payload = DecodeJwtPayload(idTokenEl.GetString());
            if (payload is null) return null;

            return payload.RootElement.TryGetProperty("https://api.openai.com/auth", out var auth) &&
                   auth.ValueKind == JsonValueKind.Object &&
                   auth.TryGetProperty("chatgpt_plan_type", out var planEl) &&
                   planEl.ValueKind == JsonValueKind.String
                ? planEl.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>JWT 의 payload(두 번째 조각, base64url) 를 JSON 으로 편다. 형태가 아니면 null.</summary>
    internal static JsonDocument? DecodeJwtPayload(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return null;

        var parts = jwt.Split('.');
        if (parts.Length < 2 || parts[1].Length == 0) return null;

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        try
        {
            return JsonDocument.Parse(Convert.FromBase64String(payload));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Direct API 로 계정 사용량을 읽는다. 못 읽으면 스냅샷 없이 그 이유를 돌려준다(#198).
    ///
    /// 토큰이 만료돼도 여기서 refresh_token 으로 갱신하거나 auth.json 에 쓰지 않는다. refresh token 은 한 번 쓰면
    /// 새 것으로 바뀌므로, 앱이 갱신하면 Codex 가 들고 있는 refresh token 이 무효가 돼 Codex 자체가 로그아웃될 수 있다.
    /// 갱신은 Codex 가 실행될 때 스스로 한다 — 앱은 실패 사유만 알린다.
    /// </summary>
    internal static async Task<(ProviderUsageSnapshot? Snapshot, CodexApiStatus Status)> FetchUsageFromApiAsync(
        HttpClient http, string authPath)
    {
        var (token, accountId) = TryGetApiCredentials(authPath);
        if (string.IsNullOrEmpty(token)) return (null, CodexApiStatus.NoCredentials);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageApiEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            // 실제 CLI 와 같은 값 — 서버가 User-Agent 로 클라이언트를 가리는지는 불명이지만,
            // Antigravity 쪽(v1internal 엔드포인트)이 이미 이 방식으로 403 을 겪은 전례가 있어 맞춰 둔다.
            request.Headers.UserAgent.ParseAdd("codex-cli");
            if (!string.IsNullOrEmpty(accountId))
                request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", accountId);

            using var response = await http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return (null, ClassifyFailedResponse(response.StatusCode, IsJwtExpired(token, DateTimeOffset.UtcNow)));

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var snapshot = ParseDirectApiUsage(doc.RootElement, DateTimeOffset.Now);
            return snapshot is null ? (null, CodexApiStatus.Unavailable) : (snapshot, CodexApiStatus.Succeeded);
        }
        catch
        {
            // 네트워크 실패·타임아웃·응답 해석 실패 — 일시적일 수 있어 로그인 문제로 보지 않는다.
            return (null, CodexApiStatus.Unavailable);
        }
    }

    /// <summary>
    /// 실패한 응답이 "다시 로그인해야 하는" 실패인지 가린다(#198).
    /// 401 은 서버가 토큰을 거부한 것이다. 403 은 원인이 여럿이다 — chatgpt.com 앞단 Cloudflare 의 챌린지·차단
    /// (1020 등), 계정 헤더 누락 같은 요청 문제도 403 이다. 그래서 403 은 access_token 이 실제로 만료됐을 때만
    /// 인증 실패로 본다. 멀쩡한 로그인에 "다시 로그인" 을 띄우면 사용자가 엉뚱한 조치를 하게 된다.
    /// 그 밖의 오류(429·5xx 등)는 일시적일 수 있어 인증 문제로 보지 않는다.
    /// </summary>
    internal static CodexApiStatus ClassifyFailedResponse(HttpStatusCode statusCode, bool isTokenExpired) => statusCode switch
    {
        HttpStatusCode.Unauthorized => CodexApiStatus.AuthFailed,
        HttpStatusCode.Forbidden when isTokenExpired => CodexApiStatus.AuthFailed,
        _ => CodexApiStatus.Unavailable,
    };

    /// <summary>
    /// access_token(JWT) 의 <c>exp</c> 가 이미 지났는지. JWT 가 아니거나 <c>exp</c> 를 못 읽으면 false(모름).
    /// 서명 검증 없이 payload 만 읽는다 — 실패 사유를 가리는 표시 판단에만 쓰고, 이 값으로 호출을 건너뛰지 않는다
    /// (PC 시계가 어긋나 있을 수 있으니 유효 여부는 서버가 정한다).
    /// </summary>
    internal static bool IsJwtExpired(string? jwt, DateTimeOffset now)
    {
        using var payload = DecodeJwtPayload(jwt);
        return payload is { RootElement.ValueKind: JsonValueKind.Object } &&
               payload.RootElement.TryGetProperty("exp", out var expEl) &&
               // TryGetInt64 는 숫자가 아닌 요소에서 false 가 아니라 예외를 던진다 — 종류를 먼저 본다.
               expEl.ValueKind == JsonValueKind.Number &&
               expEl.TryGetInt64(out var exp) &&
               exp <= now.ToUnixTimeSeconds();
    }

    /// <summary>
    /// <c>~/.codex/auth.json</c> 에서 Direct API 호출에 쓸 자격 증명을 읽는다.
    /// 실제 파일은 최상위가 아니라 <c>tokens</c> 아래에 담겨 있다(#148) — 최상위에서 읽으면 항상
    /// null 이 되어 API 경로가 조용히 죽은 채로 로그 폴백만 타게 된다. <c>account_id</c> 도 같은
    /// 자리에 있으며, 서버가 이 값 없이는 403 을 준다(codex CLI 의 ChatGPT-Account-Id 헤더와 동일).
    /// </summary>
    internal static (string? Token, string? AccountId) TryGetApiCredentials(string authPath)
    {
        try
        {
            if (!File.Exists(authPath)) return (null, null);
            using var doc = JsonDocument.Parse(ReadAuthFile(authPath));
            if (!doc.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
                return (null, null);

            var token = tokens.TryGetProperty("access_token", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            var accountId = tokens.TryGetProperty("account_id", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() : null;
            return (token, accountId);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Direct API(<c>/backend-api/wham/usage</c>) 응답을 스냅샷으로 편다.
    /// 로컬 세션 로그(<see cref="ApplyRateLimits"/>)와는 완전히 다른 스키마다 — 창이
    /// <c>rate_limit.primary_window</c>/<c>secondary_window</c> 로 한 겹 더 감싸여 있고,
    /// 리셋까지 남은 시간은 <c>limit_window_seconds</c>(초 단위, 로그의 <c>window_minutes</c>
    /// 는 분 단위)와 <c>reset_at</c>(로그의 <c>resets_at</c>과 이름이 다르다)로 불린다.
    /// (실측: openai/codex 저장소 codex-backend-openapi-models 의 OpenAPI 모델 기준, 2026-09-18)
    /// </summary>
    internal static ProviderUsageSnapshot? ParseDirectApiUsage(JsonElement root, DateTimeOffset now)
    {
        if (!root.TryGetProperty("rate_limit", out var rateLimitEl) || rateLimitEl.ValueKind != JsonValueKind.Object)
            return null;

        var windows = ReadDirectApiWindows(rateLimitEl);
        if (windows.Count == 0) return null;

        var snapshot = new ProviderUsageSnapshot
        {
            HasData = true,
            DataSource = DirectApiSource,
            ShortUsagePercent = windows[0].Percent,
            ShortResetAt = windows[0].ResetAt,
            ShortWindowMinutes = windows[0].WindowMinutes,
        };
        if (windows.Count > 1)
        {
            snapshot.LongUsagePercent = windows[1].Percent;
            snapshot.LongResetAt = windows[1].ResetAt;
            snapshot.LongWindowMinutes = windows[1].WindowMinutes;
        }

        if (root.TryGetProperty("plan_type", out var planEl) && planEl.ValueKind == JsonValueKind.String)
            snapshot.PlanType = planEl.GetString();

        ReadAdditionalRateLimits(root, snapshot.AdditionalQuotas, now);

        // API 응답이라고 리셋 시각이 항상 미래인 것은 아니다(캐시된 응답·시계 어긋남).
        DropExpiredWindows(snapshot, now);

        return snapshot;
    }

    /// <summary>
    /// 기본 한도 말고 따로 걸린 한도들(이미지 생성 등, #182)을 창 단위로 편다.
    /// 응답 최상위의 <c>additional_rate_limits: [{limit_name, metered_feature, rate_limit}]</c> 이고,
    /// <c>rate_limit</c> 은 기본 한도와 같은 <c>primary_window</c>/<c>secondary_window</c> 구조다.
    /// 공식 CLI 는 <c>metered_feature</c> 를 한도 ID 로, <c>limit_name</c> 을 표시 이름으로 쓴다
    /// (openai/codex backend-client/src/client.rs, codex-backend-openapi-models 의 AdditionalRateLimitDetails).
    /// 이미지 생성 한도의 실제 이름은 공개 자료로 확인되지 않아 이름으로 고르지 않고 전부 받는다.
    /// </summary>
    private static void ReadAdditionalRateLimits(JsonElement root, List<AdditionalQuotaWindow> target, DateTimeOffset observedAt)
    {
        if (!root.TryGetProperty("additional_rate_limits", out var limitsEl) || limitsEl.ValueKind != JsonValueKind.Array)
            return;

        foreach (var limitEl in limitsEl.EnumerateArray())
        {
            if (limitEl.ValueKind != JsonValueKind.Object) continue;

            var limitName = ReadString(limitEl, "limit_name") ?? "";
            var limitId = ReadString(limitEl, "metered_feature") ?? limitName;
            // 기본 한도가 여기 한 번 더 실려 오면 같은 게이지가 두 번 그려진다.
            if (limitId.Length == 0 || IsDefaultLimit(limitId)) continue;
            if (!limitEl.TryGetProperty("rate_limit", out var rateLimitEl) || rateLimitEl.ValueKind != JsonValueKind.Object)
                continue;

            AddAdditionalWindows(target, limitId, limitName, ReadDirectApiWindows(rateLimitEl), observedAt);
        }
    }

    // primary/secondary 를 창 길이 오름차순으로 — 기본 한도·추가 한도가 같은 규칙을 쓴다.
    private static List<RateWindow> ReadDirectApiWindows(JsonElement rateLimitEl)
    {
        var windows = new List<RateWindow>(2);
        if (ReadDirectApiWindow(rateLimitEl, "primary_window") is { } primary) windows.Add(primary);
        if (ReadDirectApiWindow(rateLimitEl, "secondary_window") is { } secondary) windows.Add(secondary);
        windows.Sort(CompareByWindowLength);
        return windows;
    }

    private static void AddAdditionalWindows(
        List<AdditionalQuotaWindow> target, string limitId, string limitName, List<RateWindow> windows,
        DateTimeOffset observedAt)
    {
        foreach (var window in windows)
        {
            target.Add(new AdditionalQuotaWindow
            {
                LimitId = limitId,
                LimitName = limitName,
                UsagePercent = window.Percent,
                ResetAt = window.ResetAt,
                WindowMinutes = window.WindowMinutes,
                ObservedAt = observedAt,
            });
        }
    }

    /// <summary>
    /// 기본 한도 ID. 세션 로그의 <c>limit_id</c> 가 이 값이거나 비어 있으면(이 필드가 생기기 전 로그) 기본 한도다
    /// (openai/codex codex-api/src/rate_limits.rs 가 기본 헤더 묶음을 이 ID 로 부른다).
    /// </summary>
    private const string DefaultLimitId = "codex";

    private static bool IsDefaultLimit(string limitId) =>
        string.Equals(limitId, DefaultLimitId, StringComparison.OrdinalIgnoreCase);

    // window_minutes 오름차순(길이를 모르는 창은 뒤로)
    private static int CompareByWindowLength(RateWindow a, RateWindow b) =>
        (a.WindowMinutes ?? int.MaxValue).CompareTo(b.WindowMinutes ?? int.MaxValue);

    // limit_window_seconds 는 초 단위라 분 단위인 로컬 로그 스키마(ReadWindow)와 맞추려면 60 으로 나눈다.
    private static RateWindow? ReadDirectApiWindow(JsonElement rateLimitEl, string windowName)
    {
        if (!rateLimitEl.TryGetProperty(windowName, out var windowEl) || windowEl.ValueKind != JsonValueKind.Object)
            return null;

        double percent = windowEl.TryGetProperty("used_percent", out var percentEl) &&
                         percentEl.ValueKind == JsonValueKind.Number
            ? Math.Clamp(percentEl.GetDouble() / 100.0, 0, 1)
            : 0;

        // 숫자가 아닌 값(null 등)은 그 필드만 "없음"이다 — 예외로 응답 전체를 버리지 않는다(#201).
        DateTimeOffset? resetAt = JsonNumber.TryReadInt64(windowEl, "reset_at", out var epoch) && epoch > 0
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;

        int? windowMinutes = JsonNumber.TryReadInt32(windowEl, "limit_window_seconds", out var ws) && ws > 0
            ? (int)Math.Round(ws / 60.0, MidpointRounding.AwayFromZero)
            : null;

        return new RateWindow(percent, resetAt, windowMinutes);
    }

    public ProviderUsageSnapshot GetTodaySnapshot() => GetTodaySnapshot(SessionsPath);

    // sessionsPath / now 를 주입받는 오버로드 — 단위 테스트에서 임시 로그 폴더와 기준 시각을 지정하기 위한 것.
    public ProviderUsageSnapshot GetTodaySnapshot(string sessionsPath, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        var snapshot = new ProviderUsageSnapshot();
        if (!Directory.Exists(sessionsPath))
        {
            snapshot.ErrorMessage = Loc.CodexSourceNotFound;
            return snapshot;
        }

        var today = reference.LocalDateTime.Date;
        var latestRateTs = DateTimeOffset.MinValue;
        var latestAdditional = new Dictionary<string, LoggedLimit>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.GetFiles(sessionsPath, "rollout-*.jsonl", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                ProcessFile(file, today, snapshot, ref latestRateTs, latestAdditional);
            }
            catch
            {
                // Skip unreadable files
            }
        }

        // 로그에는 서버의 한도 순서가 없어 ID 순으로 고정한다 — 파일 순서에 따라 게이지가 자리를 바꾸지 않게.
        foreach (var limit in latestAdditional.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
            snapshot.AdditionalQuotas.AddRange(limit.Value.Windows);

        DropExpiredWindows(snapshot, reference);

        if (!snapshot.HasData && snapshot.ErrorMessage is null)
            snapshot.ErrorMessage = Loc.CodexNoUsageToday;

        return snapshot;
    }

    /// <summary>
    /// 리셋 시각이 이미 지난 창은 통째로 버린다.
    /// 로그 스캔은 날짜와 무관하게 가장 최근의 rate_limits 를 집어오므로, 며칠 전 세션 로그에서
    /// 이미 끝난 창의 사용률과 리셋 시각을 그대로 읽어 온다. 그 값은 지금 창을 설명하지 않는다.
    /// 남겨 두면 사용률은 옛날 숫자로 표시되고 시간선은 100% 경과로 계산돼 막대 오른쪽 끝에 박힌다.
    /// (동기화가 켜져 있으면 MainViewModel 이 다른 PC 의 최신 할당량으로 이 빈자리를 채운다.)
    /// </summary>
    private static void DropExpiredWindows(ProviderUsageSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.ShortResetAt is { } shortReset && shortReset <= now)
        {
            snapshot.ShortUsagePercent = 0;
            snapshot.ShortResetAt = null;
            snapshot.ShortWindowMinutes = null;
            snapshot.IsShortResetEstimated = false;
        }

        if (snapshot.LongResetAt is { } longReset && longReset <= now)
        {
            snapshot.LongUsagePercent = 0;
            snapshot.LongResetAt = null;
            snapshot.LongWindowMinutes = null;
        }

        snapshot.AdditionalQuotas.RemoveAll(window => window.ResetAt is { } reset && reset <= now);
    }

    private static void ProcessFile(string filePath, DateTime today, ProviderUsageSnapshot snapshot,
        ref DateTimeOffset latestRateTs, Dictionary<string, LoggedLimit> latestAdditional)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        UsageTotals? prevTotals = null;
        bool fileHadTodayActivity = false;
        string? line;
        DateTimeOffset? firstTodayActivityTs = null;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "event_msg")
                    continue;
                if (!root.TryGetProperty("payload", out var payloadEl))
                    continue;
                if (!payloadEl.TryGetProperty("type", out var payloadTypeEl) || payloadTypeEl.GetString() != "token_count")
                    continue;

                var ts = root.TryGetProperty("timestamp", out var tsEl) &&
                         DateTimeOffset.TryParse(tsEl.GetString(), out var parsedTs)
                    ? parsedTs
                    : DateTimeOffset.MinValue;

                // 이름 그대로 "오늘 첫 활동" 이어야 한다. 날짜를 안 보면 며칠 전 로그의 첫 줄이 잡혀
                // 리셋 추정치가 과거로 계산되고, 시간선이 100% 경과(막대 오른쪽 끝)로 고정된다.
                if (ts != DateTimeOffset.MinValue && ts.ToLocalTime().Date == today)
                    firstTodayActivityTs ??= ts;

                if (payloadEl.TryGetProperty("rate_limits", out var rateLimitsEl) &&
                    rateLimitsEl.ValueKind == JsonValueKind.Object)
                {
                    // 최신 CLI 는 한도마다 rate_limits 줄을 따로 남기고 limit_id 로 구분한다. 이 값을 안 보면
                    // 추가 한도(이미지 생성 등) 줄이 기본 게이지를 덮어쓴다 — 기본 한도 줄만 기본 게이지에 쓰고,
                    // 나머지는 한도별 최신 줄을 추가 한도로 모은다(#182).
                    var limitId = ReadString(rateLimitsEl, "limit_id");
                    if (limitId is null || IsDefaultLimit(limitId))
                    {
                        if (ts > latestRateTs)
                        {
                            latestRateTs = ts;
                            ApplyRateLimits(rateLimitsEl, snapshot);
                        }
                    }
                    else if (!latestAdditional.TryGetValue(limitId, out var known) || ts > known.Timestamp)
                    {
                        // 읽을 창이 없는 줄이 앞서 읽은 정상 값을 지우지 않게 한다.
                        var windows = ReadLoggedAdditionalWindows(rateLimitsEl, limitId, ts);
                        if (windows.Count > 0)
                            latestAdditional[limitId] = new LoggedLimit(ts, windows);
                    }
                }

                if (!TryReadTotals(payloadEl, prevTotals, out var totals))
                    continue;

                if (ts != DateTimeOffset.MinValue && ts.ToLocalTime().Date == today)
                {
                    var delta = prevTotals is null ? totals : totals.Subtract(prevTotals.Value);
                    snapshot.TotalInputTokens += Math.Max(0, delta.InputTokens);
                    snapshot.TotalOutputTokens += Math.Max(0, delta.OutputTokens + delta.ReasoningOutputTokens);
                    snapshot.TotalCacheReadTokens += Math.Max(0, delta.CachedInputTokens);
                    snapshot.TotalCacheWriteTokens += 0;
                    snapshot.HourlyTokens[ts.ToLocalTime().Hour] += Math.Max(0, delta.TotalTokens);
                    fileHadTodayActivity = true;
                    snapshot.HasData = true;
                }

                prevTotals = totals;
            }
            catch
            {
                // Skip malformed lines
            }
        }

        if (fileHadTodayActivity)
            snapshot.SessionCount++;

        // 로그에 resets_at이 없으면 금번 초기화 전 첫 활동 시간 + 창 길이로 폴백(창 길이를 알 때만).
        // 창 길이를 모르면 임의 추정 대신 표시하지 않는다(예전 5시간 하드코딩은 주간 창에 맞지 않았음).
        if (snapshot.ShortResetAt is null && firstTodayActivityTs.HasValue &&
            snapshot.ShortWindowMinutes is int shortWindowMinutes && shortWindowMinutes > 0)
        {
            snapshot.ShortResetAt = firstTodayActivityTs.Value.AddMinutes(shortWindowMinutes);
            snapshot.IsShortResetEstimated = true;
        }
    }

    // 합계 값은 세션 누적이고, 오늘 사용량은 직전 줄과의 차분으로 센다. 그래서 읽지 못한 값을 0 으로 두면
    // 차분의 기준이 0 으로 떨어져 다음 줄이 세션 누적 전체를 오늘 사용량으로 한 번 더 더한다(#201).
    // 합계 객체가 없으면 이 줄을 쓰지 않고, 필드 하나만 숫자가 아니면(null 등) 그 필드만 직전 값을 이어 받아 증가 0 으로 둔다.
    private static bool TryReadTotals(JsonElement payloadEl, UsageTotals? previous, out UsageTotals totals)
    {
        totals = default;
        if (!payloadEl.TryGetProperty("info", out var infoEl) ||
            infoEl.ValueKind != JsonValueKind.Object ||
            !infoEl.TryGetProperty("total_token_usage", out var totalEl) ||
            totalEl.ValueKind != JsonValueKind.Object)
            return false;

        var baseline = previous ?? default;
        totals = new UsageTotals(
            ReadCounter(totalEl, "input_tokens", baseline.InputTokens),
            ReadCounter(totalEl, "cached_input_tokens", baseline.CachedInputTokens),
            ReadCounter(totalEl, "output_tokens", baseline.OutputTokens),
            ReadCounter(totalEl, "reasoning_output_tokens", baseline.ReasoningOutputTokens),
            ReadCounter(totalEl, "total_tokens", baseline.TotalTokens));
        return true;
    }

    // rate_limits의 primary/secondary 창을 window_minutes 오름차순으로 정렬해
    // 짧은 창을 Short 슬롯, 긴 창을 Long 슬롯에 배치한다. 백엔드가 슬롯에 담는 창
    // 길이를 바꿔도(예: primary가 5시간→주간) 라벨/위치가 어긋나지 않는다.
    private static void ApplyRateLimits(JsonElement rateLimitsEl, ProviderUsageSnapshot snapshot)
    {
        var windows = ReadLoggedWindows(rateLimitsEl);

        snapshot.ShortUsagePercent = 0;
        snapshot.ShortResetAt = null;
        snapshot.ShortWindowMinutes = null;
        snapshot.IsShortResetEstimated = false;
        snapshot.LongUsagePercent = 0;
        snapshot.LongResetAt = null;
        snapshot.LongWindowMinutes = null;

        if (windows.Count > 0)
        {
            snapshot.ShortUsagePercent = windows[0].Percent;
            snapshot.ShortResetAt = windows[0].ResetAt;
            snapshot.ShortWindowMinutes = windows[0].WindowMinutes;
        }
        if (windows.Count > 1)
        {
            snapshot.LongUsagePercent = windows[1].Percent;
            snapshot.LongResetAt = windows[1].ResetAt;
            snapshot.LongWindowMinutes = windows[1].WindowMinutes;
        }

        if (rateLimitsEl.TryGetProperty("plan_type", out var planEl) && planEl.ValueKind == JsonValueKind.String)
            snapshot.PlanType = planEl.GetString();
    }

    // primary/secondary 를 창 길이 오름차순으로 — Direct API 경로(ReadDirectApiWindows)와 같은 규칙.
    private static List<RateWindow> ReadLoggedWindows(JsonElement rateLimitsEl)
    {
        var windows = new List<RateWindow>(2);
        if (ReadWindow(rateLimitsEl, "primary") is { } primary) windows.Add(primary);
        if (ReadWindow(rateLimitsEl, "secondary") is { } secondary) windows.Add(secondary);
        windows.Sort(CompareByWindowLength);
        return windows;
    }

    // 로그 줄의 limit_name 은 비어 있을 수 있다(공식 TUI 도 그때는 limit_id 로 부른다).
    // 리셋 시각이 없는 창은 버린다 — 로그는 날짜와 무관하게 최신 줄을 집어오므로, 리셋 시각 없이는
    // 그 값이 지금 창을 설명하는지 가릴 수 없어 며칠 전 사용률이 그대로 남는다.
    private static List<AdditionalQuotaWindow> ReadLoggedAdditionalWindows(
        JsonElement rateLimitsEl, string limitId, DateTimeOffset observedAt)
    {
        var windows = new List<AdditionalQuotaWindow>(2);
        AddAdditionalWindows(windows, limitId, ReadString(rateLimitsEl, "limit_name") ?? "",
            [.. ReadLoggedWindows(rateLimitsEl).Where(window => window.ResetAt is not null)], observedAt);
        return windows;
    }

    private readonly record struct LoggedLimit(DateTimeOffset Timestamp, List<AdditionalQuotaWindow> Windows);

    // 창(primary/secondary)이 객체가 아니면(JSON null 포함) null 반환.
    private static RateWindow? ReadWindow(JsonElement rateLimitsEl, string windowName)
    {
        if (!rateLimitsEl.TryGetProperty(windowName, out var windowEl) ||
            windowEl.ValueKind != JsonValueKind.Object)
            return null;

        double percent = windowEl.TryGetProperty("used_percent", out var percentEl) &&
                         percentEl.ValueKind == JsonValueKind.Number
            ? Math.Clamp(percentEl.GetDouble() / 100.0, 0, 1)
            : 0;

        // epoch 가 0/음수면 "리셋 시각 없음"이다. 그대로 받으면 1970-01-01 이 되어
        // 시간선이 100% 경과로 계산되고 막대 오른쪽 끝에 박힌다(window_minutes 처럼 양수만 받는다).
        DateTimeOffset? resetAt = JsonNumber.TryReadInt64(windowEl, "resets_at", out var epoch) && epoch > 0
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;

        int? windowMinutes = JsonNumber.TryReadInt32(windowEl, "window_minutes", out var wm) && wm > 0
            ? wm
            : null;

        return new RateWindow(percent, resetAt, windowMinutes);
    }

    private readonly record struct RateWindow(double Percent, DateTimeOffset? ResetAt, int? WindowMinutes);

    // 없거나 숫자가 아니면(null 등) fallback — JsonElement.TryGetInt64 는 숫자가 아닌 값에서 false 가 아니라 예외를
    // 던지므로 종류를 먼저 보는 JsonNumber 로 읽는다. 값 하나 때문에 줄 전체가 건너뛰어지지 않는다(#201).
    private static long ReadCounter(JsonElement el, string name, long fallback) =>
        JsonNumber.TryReadInt64(el, name, out var value) ? value : fallback;

    private readonly record struct UsageTotals(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningOutputTokens,
        long TotalTokens)
    {
        public UsageTotals Subtract(UsageTotals other) => new(
            InputTokens - other.InputTokens,
            CachedInputTokens - other.CachedInputTokens,
            OutputTokens - other.OutputTokens,
            ReasoningOutputTokens - other.ReasoningOutputTokens,
            TotalTokens - other.TotalTokens);
    }
}
