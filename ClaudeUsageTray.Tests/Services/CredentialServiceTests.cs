using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class CredentialServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public CredentialServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cut-credentials-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, ".credentials.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 회귀 방지: Claude Code 가 로그아웃하면서 accessToken 만 빈 문자열로 남기는 경우가 있다.
    /// 이걸 토큰으로 취급하면 인증 없는 요청이 나가고 429 가 돌아와, 앱이 "로그인 필요"가 아니라
    /// "일시적 제한"으로 오진한 채 0% 를 계속 보여줬다.
    /// </summary>
    [Fact]
    public async Task GetValidAccessTokenAsync_ReturnsNull_WhenTokenIsEmptyString()
    {
        Write("""
        {
          "claudeAiOauth": {
            "accessToken": "",
            "refreshToken": "",
            "expiresAt": 0,
            "scopes": ["user:inference", "user:profile"],
            "subscriptionType": "max"
          }
        }
        """);

        using var service = new CredentialService(_path);

        Assert.Null(await service.GetValidAccessTokenAsync());
        Assert.Null(service.GetAccessToken());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ReturnsNull_WhenTokenIsWhitespace()
    {
        Write("""
        { "claudeAiOauth": { "accessToken": "   ", "refreshToken": "   ", "expiresAt": 0 } }
        """);

        using var service = new CredentialService(_path);

        Assert.Null(await service.GetValidAccessTokenAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ReturnsToken_WhenStillValid()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        Write($$"""
        { "claudeAiOauth": { "accessToken": "sk-test-token", "refreshToken": "rt", "expiresAt": {{expiresAt}} } }
        """);

        using var service = new CredentialService(_path);

        Assert.Equal("sk-test-token", await service.GetValidAccessTokenAsync());
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ReturnsNull_WhenFileMissing()
    {
        using var service = new CredentialService(_path);

        Assert.Null(await service.GetValidAccessTokenAsync());
    }

    [Fact]
    public void TryGetSubscriptionInfo_ReadsTierFromFile()
    {
        Write("""
        { "claudeAiOauth": { "accessToken": "t", "subscriptionType": "max", "rateLimitTier": "default_claude_max_5x" } }
        """);

        using var service = new CredentialService(_path);

        Assert.True(service.TryGetSubscriptionInfo(out var info));
        Assert.Equal("max", info.SubscriptionType);
        Assert.Equal("default_claude_max_5x", info.RateLimitTier);
    }

    // 파일이 없으면 로그아웃이 확정이다 — "판단 가능" 으로 돌려줘야 호출부가 구독 표시를 내린다.
    [Fact]
    public void TryGetSubscriptionInfo_MissingFile_IsADefiniteNoSubscription()
    {
        using var service = new CredentialService(_path);

        Assert.True(service.TryGetSubscriptionInfo(out var info));
        Assert.Null(info.SubscriptionType);
    }

    // Claude Code 가 쓰는 도중이라 반쯤 쓰인 파일 — 이걸 "구독 아님" 으로 확정하면 섹션이 깜빡인다.
    [Fact]
    public void TryGetSubscriptionInfo_UnreadableFile_IsUndecided()
    {
        Write("""{ "claudeAiOauth": { "subscriptionType": "ma""");

        using var service = new CredentialService(_path);

        Assert.False(service.TryGetSubscriptionInfo(out _));
    }

    // 회귀 방지(#154): 트레이가 Claude Code 보다 먼저 떠 ~/.claude 가 없던 PC. 감시가 시작되지 않으므로
    // 나중에 로그인해도 변경 이벤트가 오지 않는다 — 정기 새로고침이 같은 인스턴스로 다시 읽어 반영할 수 있어야 한다.
    [Fact]
    public void TryGetSubscriptionInfo_PicksUpLoginAfterServiceStartedWithoutDirectory()
    {
        var missingDir = Path.Combine(_dir, "not-yet", ".claude");
        var path = Path.Combine(missingDir, ".credentials.json");
        using var service = new CredentialService(path);
        Assert.True(service.TryGetSubscriptionInfo(out var before));
        Assert.Null(before.SubscriptionType);

        Directory.CreateDirectory(missingDir);
        File.WriteAllText(path, """{ "claudeAiOauth": { "accessToken": "t", "subscriptionType": "pro" } }""");

        Assert.True(service.TryGetSubscriptionInfo(out var after));
        Assert.Equal("pro", after.SubscriptionType);
    }

    // Claude Code 가 파일을 쓰기 전용으로 열어 둔 순간에도 읽기가 막히지 않아야 한다(FileShare.ReadWrite).
    [Fact]
    public void TryGetSubscriptionInfo_ReadsWhileAnotherWriterHoldsTheFile()
    {
        Write("""{ "claudeAiOauth": { "accessToken": "t", "subscriptionType": "pro" } }""");
        using var writer = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        using var service = new CredentialService(_path);

        Assert.True(service.TryGetSubscriptionInfo(out var info));
        Assert.Equal("pro", info.SubscriptionType);
    }

    // 회귀 방지(#177): 만료 토큰의 갱신이 429 로 막히면 폴링(2분)마다 다시 두드려 제한이 풀리지 않았다.
    // Retry-After 동안은 토큰 엔드포인트를 다시 부르지 않고 기존 토큰으로 폴백해야 한다.
    [Fact]
    public async Task GetValidAccessTokenAsync_BacksOffAfterRateLimitedRefresh()
    {
        WriteExpired("rt-1");
        var handler = new StubHandler(_ => Respond(HttpStatusCode.TooManyRequests,
            """{"error":{"type":"rate_limit_error","message":"Rate limited."}}""", retryAfterSeconds: 600));
        using var service = new CredentialService(_path, handler);

        Assert.Equal("expired-token", await service.GetValidAccessTokenAsync());
        Assert.Equal("expired-token", await service.GetValidAccessTokenAsync());

        Assert.Equal(1, handler.Calls);
        var failure = Assert.IsType<TokenRefreshFailure>(service.RefreshFailure);
        Assert.Equal(TokenRefreshFailureKind.RateLimited, failure.Kind);
        Assert.InRange(failure.RetryAtUtc, DateTimeOffset.UtcNow.AddSeconds(590), DateTimeOffset.UtcNow.AddSeconds(610));
    }

    // 다시 로그인해 refresh 토큰이 바뀌면 이전 토큰의 백오프를 기다리지 않고 바로 갱신한다.
    [Fact]
    public async Task GetValidAccessTokenAsync_RetriesImmediately_WhenRefreshTokenChanges()
    {
        WriteExpired("rt-1");
        var handler = new StubHandler(_ => Respond(HttpStatusCode.TooManyRequests, "{}"));
        using var service = new CredentialService(_path, handler);
        await service.GetValidAccessTokenAsync();

        WriteExpired("rt-2");
        await service.GetValidAccessTokenAsync();

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetValidAccessTokenAsync_ClassifiesInvalidGrantAsRejected()
    {
        WriteExpired("rt-1");
        var handler = new StubHandler(_ => Respond(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Refresh token revoked"}"""));
        using var service = new CredentialService(_path, handler);

        await service.GetValidAccessTokenAsync();

        Assert.Equal(TokenRefreshFailureKind.Rejected, service.RefreshFailure?.Kind);
    }

    // 성공하면 새 토큰을 저장하고 실패 상태를 지운다. 요청에는 Claude Code 처럼 저장된 scope 를 싣는다.
    [Fact]
    public async Task GetValidAccessTokenAsync_PersistsRefreshedTokenAndSendsScope()
    {
        WriteExpired("rt-1");
        string? requestBody = null;
        var handler = new StubHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Respond(HttpStatusCode.OK,
                """{"access_token":"new-token","refresh_token":"rt-new","expires_in":28800}""");
        });
        using var service = new CredentialService(_path, handler);

        Assert.Equal("new-token", await service.GetValidAccessTokenAsync());

        Assert.Null(service.RefreshFailure);
        Assert.Contains("\"scope\":\"user:inference user:profile\"", requestBody);
        var saved = File.ReadAllText(_path);
        Assert.Contains("new-token", saved);
        Assert.Contains("rt-new", saved);
        Assert.Contains("\"subscriptionType\": \"max\"", saved);
    }

    [Theory]
    [InlineData(1, null, 300)]
    [InlineData(2, null, 600)]
    [InlineData(4, null, 2400)]
    [InlineData(5, null, 3600)]
    [InlineData(50, null, 3600)]
    [InlineData(1, 900, 900)]
    [InlineData(3, 60, 1200)]
    public void ComputeRefreshBackoffSeconds_DoublesUpToCapAndHonorsRetryAfter(int failures, int? retryAfter, int expected)
    {
        Assert.Equal(expected, CredentialService.ComputeRefreshBackoffSeconds(failures, retryAfter));
    }

    private void WriteExpired(string refreshToken)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds();
        Write($$"""
        {
          "claudeAiOauth": {
            "accessToken": "expired-token",
            "refreshToken": "{{refreshToken}}",
            "expiresAt": {{expiresAt}},
            "scopes": ["user:inference", "user:profile"],
            "subscriptionType": "max"
          }
        }
        """);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body, int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
        if (retryAfterSeconds is { } seconds)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }

    private void Write(string json) => File.WriteAllText(_path, json);
}
