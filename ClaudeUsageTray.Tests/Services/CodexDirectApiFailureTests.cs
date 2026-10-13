using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Codex Direct API 가 실패했을 때 그 이유를 가르는 규칙(#198).
/// 서버가 토큰을 거부한 경우만 "다시 로그인" 안내 대상이고, 로그인 파일이 없거나 일시적으로 실패한 경우는 아니다.
/// 어느 쪽이든 기본 게이지는 로컬 로그로 계속 보여야 한다.
/// </summary>
public sealed class CodexDirectApiFailureTests : IDisposable
{
    private const string UsageEndpoint = "https://chatgpt.com/backend-api/wham/usage";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-api-failure-tests-{Guid.NewGuid():N}");
    private readonly string _authPath;
    private readonly string _sessionsPath;

    public CodexDirectApiFailureTests()
    {
        Directory.CreateDirectory(_root);
        _authPath = Path.Combine(_root, "auth.json");
        _sessionsPath = Path.Combine(_root, "sessions");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // 401 은 언제나 토큰 거부다. 403 은 Cloudflare 챌린지·차단, 계정 헤더 누락 등 원인이 여럿이라 토큰이 실제로
    // 만료됐을 때만 로그인 문제로 본다. 429·5xx 는 토큰 상태와 무관하게 일시적인 실패로 본다.
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, false, CodexApiStatus.AuthFailed)]
    [InlineData(HttpStatusCode.Unauthorized, true, CodexApiStatus.AuthFailed)]
    [InlineData(HttpStatusCode.Forbidden, false, CodexApiStatus.Unavailable)]
    [InlineData(HttpStatusCode.Forbidden, true, CodexApiStatus.AuthFailed)]
    [InlineData(HttpStatusCode.TooManyRequests, true, CodexApiStatus.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, true, CodexApiStatus.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, false, CodexApiStatus.Unavailable)]
    public void ClassifyFailedResponse_OnlyRejectedTokensNeedSignIn(
        HttpStatusCode statusCode, bool isTokenExpired, CodexApiStatus expected) =>
        Assert.Equal(expected, CodexUsageMonitor.ClassifyFailedResponse(statusCode, isTokenExpired));

    // 화면 연결 고정: 안내는 서버가 토큰을 거부했을 때만 띄운다.
    [Theory]
    [InlineData(CodexApiStatus.NotAttempted, false)]
    [InlineData(CodexApiStatus.Succeeded, false)]
    [InlineData(CodexApiStatus.NoCredentials, false)]
    [InlineData(CodexApiStatus.AuthFailed, true)]
    [InlineData(CodexApiStatus.Unavailable, false)]
    public void NeedsSignInFor_OnlyWhenTheServerRejectedTheToken(CodexApiStatus status, bool expected) =>
        Assert.Equal(expected, CodexViewModel.NeedsSignInFor(status));

    [Fact]
    public void IsJwtExpired_ComparesExpWithNow()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        Assert.True(CodexUsageMonitor.IsJwtExpired(Jwt("""{"exp":1799999999}"""), now));
        Assert.True(CodexUsageMonitor.IsJwtExpired(Jwt("""{"exp":1800000000}"""), now));
        Assert.False(CodexUsageMonitor.IsJwtExpired(Jwt("""{"exp":1800000001}"""), now));
    }

    // 모르면 "만료 아님" — 만료라고 단정할 근거가 없으면 403 을 로그인 문제로 몰지 않는다.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("opaque-access-token")]
    [InlineData("header.%%%.signature")]
    public void IsJwtExpired_IsFalseWhenTokenIsNotAJwt(string? token) =>
        Assert.False(CodexUsageMonitor.IsJwtExpired(token, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData("""{"sub":"user"}""")]
    [InlineData("""{"exp":"soon"}""")]
    [InlineData("""{"exp":99999999999999999999}""")]
    [InlineData("""[1,2]""")]
    public void IsJwtExpired_IsFalseWhenExpIsMissingOrMalformed(string payloadJson) =>
        Assert.False(CodexUsageMonitor.IsJwtExpired(Jwt(payloadJson), DateTimeOffset.UtcNow));

    [Fact]
    public async Task Fetch_WithoutAuthFile_IsNoCredentials_AndDoesNotCallTheServer()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("서버를 부르면 안 된다"));

        var (snapshot, status) = await FetchAsync(handler);

        Assert.Null(snapshot);
        Assert.Equal(CodexApiStatus.NoCredentials, status);
        Assert.Empty(handler.RequestUris);
    }

    // API 키 모드로 쓰면 auth.json 은 있어도 ChatGPT 토큰이 없다 — 로그인 만료가 아니다.
    [Fact]
    public async Task Fetch_ApiKeyModeWithoutTokens_IsNoCredentials()
    {
        File.WriteAllText(_authPath, """{"auth_mode":"apikey","OPENAI_API_KEY":null,"tokens":null}""");
        var handler = new StubHandler(_ => throw new InvalidOperationException("서버를 부르면 안 된다"));

        var (_, status) = await FetchAsync(handler);

        Assert.Equal(CodexApiStatus.NoCredentials, status);
        Assert.Empty(handler.RequestUris);
    }

    // 실제 사례: access_token 이 만료되고 Codex 도 refresh 에 실패한 PC 에서 wham/usage 가 401 을 줬다.
    // 앱은 refresh_token 으로 갱신하지 않는다 — refresh token 이 회전돼 Codex 자체가 로그아웃될 수 있다.
    [Fact]
    public async Task Fetch_Unauthorized_IsAuthFailed_AndNeverRefreshesOrWritesTheAuthFile()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(-3)));
        var before = File.ReadAllBytes(_authPath);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"detail":"Could not validate credentials"}"""),
        });

        var (snapshot, status) = await FetchAsync(handler);

        Assert.Null(snapshot);
        Assert.Equal(CodexApiStatus.AuthFailed, status);
        Assert.Equal(new Uri(UsageEndpoint), Assert.Single(handler.RequestUris));
        Assert.Equal(before, File.ReadAllBytes(_authPath));
    }

    // 토큰이 살아 있는데 403 이면(Cloudflare 차단 1020·계정 헤더 누락 등) 다시 로그인해도 풀리지 않는다.
    [Fact]
    public async Task Fetch_Forbidden_WithLiveToken_IsUnavailable()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(3)));

        var (_, status) = await FetchAsync(new StubHandler(_ => CloudflareBlocked()));

        Assert.Equal(CodexApiStatus.Unavailable, status);
    }

    // JWT 가 아닌 토큰은 만료 여부를 모른다 — 403 만으로 로그인 문제라고 단정하지 않는다.
    [Fact]
    public async Task Fetch_Forbidden_WithOpaqueToken_IsUnavailable()
    {
        WriteAuth("opaque-access-token");

        var (_, status) = await FetchAsync(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        Assert.Equal(CodexApiStatus.Unavailable, status);
    }

    [Fact]
    public async Task Fetch_Forbidden_WithExpiredToken_IsAuthFailed()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(-3)));

        var (_, status) = await FetchAsync(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        Assert.Equal(CodexApiStatus.AuthFailed, status);
    }

    [Fact]
    public async Task Fetch_ServerError_IsUnavailable()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(-3)));

        var (_, status) = await FetchAsync(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        Assert.Equal(CodexApiStatus.Unavailable, status);
    }

    [Fact]
    public async Task Fetch_NetworkFailure_IsUnavailable()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(3)));

        var (snapshot, status) = await FetchAsync(new StubHandler(_ => throw new HttpRequestException("offline")));

        Assert.Null(snapshot);
        Assert.Equal(CodexApiStatus.Unavailable, status);
    }

    // 200 이어도 읽을 창이 없으면(스키마 변경 등) 성공으로 치지 않는다 — 로그인 문제도 아니다.
    [Fact]
    public async Task Fetch_SuccessWithoutRateLimit_IsUnavailable()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(3)));

        var (snapshot, status) = await FetchAsync(new StubHandler(_ => Ok("""{"plan_type":"plus"}""")));

        Assert.Null(snapshot);
        Assert.Equal(CodexApiStatus.Unavailable, status);
    }

    [Fact]
    public async Task Fetch_Success_IsSucceeded()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(3)));

        var (snapshot, status) = await FetchAsync(new StubHandler(_ => Ok(UsageBody(usedPercent: 42))));

        Assert.Equal(CodexApiStatus.Succeeded, status);
        Assert.NotNull(snapshot);
        Assert.Equal(0.42, snapshot!.ShortUsagePercent, 3);
    }

    // 완료 기준: 만료된 로그인에서는 로컬 로그 게이지를 그대로 보여 주면서 안내 사유(AuthFailed)를 싣는다.
    // 오류 메시지는 만들지 않는다 — 빨간 오류가 아니라 안내다.
    [Fact]
    public async Task Snapshot_AuthFailed_FallsBackToLocalLog_KeepsTheGauge_AndCarriesTheReason()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(-3)));
        WriteTodayLog(primaryPercent: 30);

        var snapshot = await SnapshotAsync(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        Assert.Equal(CodexApiStatus.AuthFailed, snapshot.CodexApiStatus);
        Assert.True(CodexViewModel.NeedsSignInFor(snapshot.CodexApiStatus));
        Assert.Equal("Local Log", snapshot.DataSource);
        Assert.True(snapshot.HasData);
        Assert.Equal(0.30, snapshot.ShortUsagePercent, 3);
        Assert.Null(snapshot.ErrorMessage);
    }

    [Fact]
    public async Task Snapshot_NetworkFailure_FallsBackWithoutTheSignInReason()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(3)));
        WriteTodayLog(primaryPercent: 30);

        var snapshot = await SnapshotAsync(new StubHandler(_ => throw new HttpRequestException("offline")));

        Assert.Equal(CodexApiStatus.Unavailable, snapshot.CodexApiStatus);
        Assert.False(CodexViewModel.NeedsSignInFor(snapshot.CodexApiStatus));
        Assert.Equal("Local Log", snapshot.DataSource);
        Assert.Equal(0.30, snapshot.ShortUsagePercent, 3);
    }

    // 정상 토큰이면 API 값을 쓰고 안내 사유가 사라진다.
    [Fact]
    public async Task Snapshot_Success_UsesTheApi()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(3)));
        WriteTodayLog(primaryPercent: 30);

        var snapshot = await SnapshotAsync(new StubHandler(_ => Ok(UsageBody(usedPercent: 42))));

        Assert.Equal(CodexApiStatus.Succeeded, snapshot.CodexApiStatus);
        Assert.Equal(CodexUsageMonitor.DirectApiSource, snapshot.DataSource);
        Assert.Equal(0.42, snapshot.ShortUsagePercent, 3);
        Assert.Equal(100, snapshot.TotalInputTokens); // 토큰 집계는 여전히 로컬 로그에서 온다
    }

    [Fact]
    public async Task Snapshot_DirectApiDisabled_IsNotAttempted_AndDoesNotCallTheServer()
    {
        WriteAuth(AccessToken(DateTimeOffset.UtcNow.AddDays(-3)));
        WriteTodayLog(primaryPercent: 30);
        var handler = new StubHandler(_ => throw new InvalidOperationException("서버를 부르면 안 된다"));

        using var http = new HttpClient(handler);
        var snapshot = await new CodexUsageMonitor().GetTodaySnapshotAsync(false, http, _authPath, _sessionsPath);

        Assert.Equal(CodexApiStatus.NotAttempted, snapshot.CodexApiStatus);
        Assert.Empty(handler.RequestUris);
    }

    private async Task<(ProviderUsageSnapshot? Snapshot, CodexApiStatus Status)> FetchAsync(StubHandler handler)
    {
        using var http = new HttpClient(handler);
        return await CodexUsageMonitor.FetchUsageFromApiAsync(http, _authPath);
    }

    private async Task<ProviderUsageSnapshot> SnapshotAsync(StubHandler handler)
    {
        using var http = new HttpClient(handler);
        return await new CodexUsageMonitor().GetTodaySnapshotAsync(true, http, _authPath, _sessionsPath);
    }

    private void WriteAuth(string accessToken) =>
        File.WriteAllText(_authPath,
            "{\"tokens\":{\"access_token\":\"" + accessToken + "\",\"account_id\":\"acc-test\"}}");

    /// <summary>오늘 로컬 시각의 token_count 한 줄 — 기본 한도(5시간 창)와 토큰 집계를 함께 싣는다.</summary>
    private void WriteTodayLog(double primaryPercent)
    {
        var dir = Path.Combine(_sessionsPath, "2026", "10", "13");
        Directory.CreateDirectory(dir);
        var ts = DateTime.Today.AddHours(10).ToUniversalTime()
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var resetsAt = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds();
        var line = string.Format(CultureInfo.InvariantCulture,
            """{{"timestamp":"{0}","type":"event_msg","payload":{{"type":"token_count","info":{{"total_token_usage":{{"input_tokens":100,"cached_input_tokens":0,"output_tokens":10,"reasoning_output_tokens":0,"total_tokens":110}}}},"rate_limits":{{"limit_id":"codex","primary":{{"used_percent":{1},"window_minutes":300,"resets_at":{2}}},"secondary":null}}}}}}""",
            ts, primaryPercent, resetsAt);
        File.WriteAllLines(Path.Combine(dir, "rollout-today.jsonl"), [line]);
    }

    private static string UsageBody(int usedPercent) => $$"""
        {
            "plan_type": "plus",
            "rate_limit": {
                "primary_window": {
                    "used_percent": {{usedPercent}},
                    "limit_window_seconds": 18000,
                    "reset_at": {{DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds()}}
                }
            }
        }
        """;

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Cloudflare 가 요청을 막을 때의 모양 — 403 + HTML 오류 페이지(Error 1020).
    private static HttpResponseMessage CloudflareBlocked() =>
        new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("<html><title>Access denied | Error 1020</title></html>", Encoding.UTF8, "text/html"),
        };

    private static string AccessToken(DateTimeOffset expiresAt) =>
        Jwt("{\"exp\":" + expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "}");

    /// <summary>서명 없는 테스트용 JWT(payload 만 base64url).</summary>
    private static string Jwt(string payloadJson) =>
        "header." +
        Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_') +
        ".signature";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri?> RequestUris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUris.Add(request.RequestUri);
            return Task.FromResult(respond(request));
        }
    }
}
