using System.Net;
using System.Net.Http;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Claude 사용량 API 가 401·5xx 를 이어서 돌려줄 때 2분마다 같은 요청을 두드리지 않는다(#99).
/// 첫 실패는 평소 주기대로 다시 묻고, 이어지면 4·8·16·30분 쉰다. 토큰이 바뀌거나 성공하면 바로 푼다.
/// </summary>
public class UsageApiServiceBackoffTests
{
    private const string UsageJson = """{"five_hour":{"utilization":12.0,"resets_at":"2026-11-21T10:00:00Z"}}""";

    private sealed class Harness
    {
        public DateTimeOffset Now = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
        public string Token = "token-1";
        public int Requests;
        public HttpStatusCode Status = HttpStatusCode.Unauthorized;
        public UsageApiService Service { get; }

        public Harness()
        {
            var http = new HttpClient(new StubHandler(_ =>
            {
                Requests++;
                return new HttpResponseMessage(Status)
                {
                    Content = new StringContent(Status == HttpStatusCode.OK ? UsageJson : """{"error":{"type":"authentication_error"}}"""),
                };
            }));
            Service = new UsageApiService(() => Task.FromResult<string?>(Token), http, () => Now);
        }

        /// <summary>한 번 묻고, 실제로 요청이 나갔는지 돌려준다.</summary>
        public async Task<bool> PollAsync()
        {
            var before = Requests;
            await Service.FetchUsageAsync();
            return Requests > before;
        }
    }

    [Fact]
    public async Task RepeatedUnauthorized_BacksOffWithGrowingSteps()
    {
        var h = new Harness();

        Assert.True(await h.PollAsync());              // 1번째 실패 — 쉬지 않는다
        Assert.True(await h.PollAsync());              // 2번째 실패 — 4분 쉼
        Assert.Equal(h.Now.AddMinutes(4), h.Service.FailureRetryAtUtc);

        h.Now = h.Now.AddMinutes(2);
        Assert.False(await h.PollAsync());             // 쉬는 중: 요청 없음, 지난 오류 유지
        Assert.StartsWith("HTTP 401", h.Service.LastError);
        Assert.Equal(0, h.Service.LastRetryAfterSeconds); // 429 쿨다운 안내로 라우팅되지 않는다

        h.Now = h.Now.AddMinutes(2);
        Assert.True(await h.PollAsync());              // 3번째 실패 — 8분 쉼
        Assert.Equal(h.Now.AddMinutes(8), h.Service.FailureRetryAtUtc);

        for (var i = 0; i < 5; i++)
        {
            h.Now = h.Service.FailureRetryAtUtc!.Value;
            Assert.True(await h.PollAsync());
        }
        // 상한은 30분이다.
        Assert.Equal(h.Now.AddMinutes(30), h.Service.FailureRetryAtUtc);
    }

    [Fact]
    public async Task NewToken_IsTriedRightAway()
    {
        var h = new Harness();
        await h.PollAsync();
        await h.PollAsync();
        Assert.NotNull(h.Service.FailureRetryAtUtc);

        h.Token = "token-2"; // Claude Code 가 토큰을 갱신했다
        Assert.True(await h.PollAsync());
        // 새 토큰의 첫 실패라 다시 처음 단계(쉬지 않음)부터 센다.
        Assert.Null(h.Service.FailureRetryAtUtc);
    }

    [Fact]
    public async Task Success_AndReset_ClearTheBackoff()
    {
        var h = new Harness();
        await h.PollAsync();
        await h.PollAsync();
        Assert.False(await h.PollAsync());

        h.Service.ResetFailureBackoff(); // 자격 파일이 바뀜(재로그인 등)
        h.Status = HttpStatusCode.OK;
        Assert.True(await h.PollAsync());
        Assert.Null(h.Service.LastError);

        h.Status = HttpStatusCode.Unauthorized;
        Assert.True(await h.PollAsync());
        Assert.True(await h.PollAsync()); // 성공으로 횟수가 초기화돼 다시 1·2번째 실패
        Assert.NotNull(h.Service.FailureRetryAtUtc);
    }

    [Fact]
    public async Task ServerErrors_BackOff_ButRateLimitKeepsItsOwnCooldown()
    {
        var h = new Harness { Status = HttpStatusCode.BadGateway };
        await h.PollAsync();
        await h.PollAsync();
        Assert.NotNull(h.Service.FailureRetryAtUtc);

        var limited = new Harness { Status = HttpStatusCode.TooManyRequests };
        await limited.PollAsync();
        await limited.PollAsync();
        Assert.Null(limited.Service.FailureRetryAtUtc);
        Assert.Equal(AppConstants.DefaultRateLimitBackoffSeconds, limited.Service.LastRetryAfterSeconds);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
