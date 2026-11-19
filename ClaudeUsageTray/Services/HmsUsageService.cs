using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

/// <summary>100milionseek 의 오늘(UTC) 토큰 한도 한 칸.</summary>
public sealed record HmsDailyUsage(
    long Used,
    long Limit,
    long Remaining,
    long Requests,
    DateTimeOffset ResetAt,
    DateTimeOffset? BonusEndsAt)
{
    public double UsagePercent => Limit <= 0 ? 0 : Math.Clamp((double)Used / Limit, 0, 1);
}

public enum HmsReadState
{
    /// <summary>설정에 키가 없다 — 화면에 아무것도 그리지 않는다.</summary>
    NoKey,
    Ok,
    /// <summary>사이트가 키를 거절했다(교체·삭제된 키). 키를 바꿀 때까지 다시 시도하지 않는다.</summary>
    InvalidKey,
    /// <summary>네트워크·서버 오류나 응답 형식이 달라 이번에는 못 읽었다.</summary>
    Unavailable,
}

/// <param name="KeyId">읽을 때 쓴 키의 지문(<see cref="HmsUsageService.KeyFingerprint"/>). 키가 없으면 null.
/// 화면이 마지막 값을 이어 보여 줄 때 같은 키의 값인지 가리는 데 쓴다 — 키 원문은 담지 않는다.</param>
public sealed record HmsReadResult(HmsReadState State, HmsDailyUsage? Usage = null, string? KeyId = null);

/// <summary>
/// OpenCode 에서 쓰는 무료 DeepSeek 중계 API(100milionseek.top)의 오늘 토큰 한도를 읽는다(#306).
/// </summary>
/// <remarks>
/// 공개 API 가 아니라 사이트 대시보드가 쓰는 내부 API 다. 대시보드와 같은 순서로 부른다 —
/// <c>POST /api/login</c> 에 API 키를 보내 <c>hm_session</c> 쿠키를 받고, 그 쿠키로 <c>GET /api/me</c>.
/// <c>/api/me</c> 는 Bearer 키를 받지 않는다(2026-11-19 확인, 401 <c>not_signed_in</c>).
/// 조회할 때마다 로그인하면 사이트에 세션이 쌓이므로 쿠키는 메모리에 두고 401 일 때만 다시 로그인한다.
/// 키는 사이트 자신에게만 보내고 로그·오류 문구에 남기지 않는다.
/// 확인이 실패하면(5xx·429·네트워크·형식 변경) 다음 시도까지 1·2·4·8·16·30분씩 쉰다 — 사이트 장애 동안
/// 매 폴링마다 로그인·조회를 두드리지 않기 위해서다(#310). 429 의 <c>Retry-After</c> 가 더 길면 그걸 따른다(상한 1시간).
/// 성공하거나 키가 바뀌면 바로 처음부터 다시 시도한다.
/// </remarks>
public sealed class HmsUsageService : IDisposable
{
    internal static readonly Uri BaseUri = new("https://100milionseek.top/");
    /// <summary>
    /// 휴대폰 피드의 provider 값(#318). 서비스 이름 그대로 — 업데이트하지 않은 휴대폰 앱도 카드 이름으로 알아보고,
    /// 공식 DeepSeek API 를 나중에 따로 넣어도 겹치지 않는다.
    /// </summary>
    public const string MobileProvider = "100milionseek";
    internal const string LoginPath = "api/login";
    internal const string MePath = "api/me";
    internal const string SessionCookieName = "hm_session";
    /// <summary>대시보드와 같은 브라우저 UA — 기본 .NET UA 는 Cloudflare 가 막을 수 있다.</summary>
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";
    /// <summary>재시도 간격 상한. 하루 한도라 이보다 오래 쉬면 오늘 값을 너무 늦게 본다.</summary>
    internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(30);
    /// <summary>사이트가 <c>Retry-After</c> 로 더 오래 쉬라고 해도 이 이상은 기다리지 않는다.</summary>
    internal static readonly TimeSpan MaxServerRetryAfter = TimeSpan.FromHours(1);

    private readonly Func<string?> _keyProvider;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    /// <summary>지금 쿠키를 받은 키. 설정에서 키가 바뀌면 쿠키를 버린다.</summary>
    private string? _sessionKey;
    private string? _sessionCookie;
    /// <summary>사이트가 거절한 키. 같은 키로는 로그인을 다시 두드리지 않는다.</summary>
    private string? _rejectedKey;
    private readonly Func<DateTimeOffset> _clock;
    /// <summary>실패가 이어진 키와 횟수·다음 시도 시각. 키가 바뀌면 버린다.</summary>
    private string? _failedKey;
    private int _consecutiveFailures;
    private DateTimeOffset _retryAt;
    /// <summary>이번 시도에서 사이트가 429 와 함께 준 <c>Retry-After</c>.</summary>
    private TimeSpan? _serverRetryAfter;

    public HmsUsageService(Func<string?> keyProvider) : this(keyProvider, new HttpClientHandler())
    {
    }

    internal HmsUsageService(Func<string?> keyProvider, HttpMessageHandler handler, Func<DateTimeOffset>? clock = null)
    {
        _keyProvider = keyProvider;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (handler is HttpClientHandler clientHandler)
        {
            // 쿠키는 직접 다룬다 — 세션 쿠키 하나만 보내고, 키가 바뀌면 바로 버리기 위해서다.
            clientHandler.UseCookies = false;
            clientHandler.AllowAutoRedirect = false;
        }
        _http = new HttpClient(handler)
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(AppConstants.ApiTimeoutSeconds),
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserUserAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<HmsReadResult> ReadAsync(CancellationToken ct = default)
    {
        var key = _keyProvider()?.Trim();
        var keyId = string.IsNullOrEmpty(key) ? null : KeyFingerprint(key);
        await _gate.WaitAsync(ct);
        try
        {
            if (string.IsNullOrEmpty(key))
            {
                ForgetSession();
                _rejectedKey = null;
                ForgetFailures();
                return new HmsReadResult(HmsReadState.NoKey);
            }
            if (!string.Equals(key, _sessionKey, StringComparison.Ordinal)) ForgetSession();
            if (string.Equals(key, _rejectedKey, StringComparison.Ordinal))
                return new HmsReadResult(HmsReadState.InvalidKey, KeyId: keyId);
            if (!string.Equals(key, _failedKey, StringComparison.Ordinal)) ForgetFailures();
            else if (_clock() < _retryAt) return new HmsReadResult(HmsReadState.Unavailable, KeyId: keyId);

            _serverRetryAfter = null;
            if (await ReadOnceAsync(key, ct) is { } usage)
            {
                ForgetFailures();
                return new HmsReadResult(HmsReadState.Ok, usage, keyId);
            }
            if (string.Equals(key, _rejectedKey, StringComparison.Ordinal))
                return new HmsReadResult(HmsReadState.InvalidKey, KeyId: keyId);
            RegisterFailure(key);
            return new HmsReadResult(HmsReadState.Unavailable, KeyId: keyId);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            RegisterFailure(key!);
            return new HmsReadResult(HmsReadState.Unavailable, KeyId: keyId);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>로그인(필요하면)과 <c>/api/me</c> 한 번. 못 읽었으면 null — 키 거절이면 <see cref="_rejectedKey"/> 가 남는다.</summary>
    private async Task<HmsDailyUsage?> ReadOnceAsync(string key, CancellationToken ct)
    {
        // 저장된 쿠키가 만료됐으면 한 번만 다시 로그인한다.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_sessionCookie == null && await LoginAsync(key, ct) != HmsReadState.Ok) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, MePath);
            request.Headers.Add("Cookie", $"{SessionCookieName}={_sessionCookie}");
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ForgetSession();
                continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                NoteRetryAfter(response);
                return null;
            }
            return ParseMe(await response.Content.ReadAsStringAsync(ct));
        }
        return null;
    }

    /// <summary>실패를 세고 다음 시도 시각을 잡는다.</summary>
    private void RegisterFailure(string key)
    {
        _failedKey = key;
        _consecutiveFailures++;
        _retryAt = _clock() + RetryDelayFor(_consecutiveFailures, _serverRetryAfter);
    }

    private void ForgetFailures()
    {
        _failedKey = null;
        _consecutiveFailures = 0;
        _retryAt = default;
    }

    /// <summary>n 번째 연속 실패 뒤의 대기 — 1·2·4·8·16·30분. 사이트가 더 길게 쉬라고 했으면 그만큼(상한 1시간).</summary>
    internal static TimeSpan RetryDelayFor(int consecutiveFailures, TimeSpan? serverRetryAfter = null)
    {
        var minutes = Math.Min(1 << Math.Clamp(consecutiveFailures - 1, 0, 5), MaxRetryDelay.TotalMinutes);
        var backoff = TimeSpan.FromMinutes(minutes);
        if (serverRetryAfter is not { } server || server <= backoff) return backoff;
        return server < MaxServerRetryAfter ? server : MaxServerRetryAfter;
    }

    private void NoteRetryAfter(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.TooManyRequests) return;
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta) _serverRetryAfter = delta;
        else if (retryAfter?.Date is { } date) _serverRetryAfter = date - _clock();
    }

    private async Task<HmsReadState> LoginAsync(string key, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new Dictionary<string, string> { ["api_key"] = key });
        using var request = new HttpRequestMessage(HttpMethod.Post, LoginPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest
            && IsInvalidKeyResponse(await response.Content.ReadAsStringAsync(ct)))
        {
            _rejectedKey = key;
            return HmsReadState.InvalidKey;
        }
        if (!response.IsSuccessStatusCode)
        {
            NoteRetryAfter(response);
            return HmsReadState.Unavailable;
        }

        var cookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? FindSessionCookie(values)
            : null;
        if (cookie == null) return HmsReadState.Unavailable;

        _sessionKey = key;
        _sessionCookie = cookie;
        return HmsReadState.Ok;
    }

    /// <summary>키를 구별하기 위한 짧은 지문(SHA-256 앞 16자리). 키 원문을 메모리 밖 상태로 퍼뜨리지 않으려고 쓴다.</summary>
    internal static string KeyFingerprint(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];

    private void ForgetSession()
    {
        _sessionKey = null;
        _sessionCookie = null;
    }

    /// <summary>
    /// 로그인 거절이 키 탓인가. 사이트는 <c>invalid_api_key</c> 코드를 준다. 코드가 없거나 다른 401(잠시 막힘 등)은
    /// 키를 탓하지 않고 다음 조회에 다시 시도한다.
    /// </summary>
    internal static bool IsInvalidKeyResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                && code.GetString() is "invalid_api_key";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary><c>Set-Cookie</c> 줄들에서 세션 쿠키 값만 꺼낸다. 비었거나 지우는 쿠키면 null.</summary>
    internal static string? FindSessionCookie(IEnumerable<string> setCookieHeaders)
    {
        foreach (var header in setCookieHeaders)
        {
            var pair = header.Split(';', 2)[0].Trim();
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            if (!string.Equals(pair[..eq].Trim(), SessionCookieName, StringComparison.Ordinal)) continue;
            var value = pair[(eq + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }
        return null;
    }

    /// <summary>
    /// <c>/api/me</c> 응답에서 오늘 한도를 읽는다. 형식이 다르면(필드 누락·한도 0) null — 화면은 "확인 불가".
    /// </summary>
    internal static HmsDailyUsage? ParseMe(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("today", out var today)
                || today.ValueKind != JsonValueKind.Object)
                return null;

            if (ReadLong(today, "limit") is not long limit || limit <= 0) return null;
            if (ReadLong(today, "used") is not long used || used < 0) return null;
            if (ReadTime(today, "reset_at") is not DateTimeOffset resetAt) return null;
            var remaining = ReadLong(today, "remaining") ?? Math.Max(0, limit - used);
            var requests = ReadLong(today, "requests") ?? 0;

            DateTimeOffset? bonusEndsAt = null;
            if (doc.RootElement.TryGetProperty("bonus", out var bonus) && bonus.ValueKind == JsonValueKind.Object)
                bonusEndsAt = ReadTime(bonus, "ends_at");

            return new HmsDailyUsage(used, limit, remaining, requests, resetAt, bonusEndsAt);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? ReadLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)
            ? n
            : null;

    private static DateTimeOffset? ReadTime(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time)
            ? time
            : null;

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
