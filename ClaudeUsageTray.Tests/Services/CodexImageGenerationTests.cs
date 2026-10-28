using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// ChatGPT 이미지 생성 잔여 횟수(#206). wham/usage 에는 이 값이 없어 같은 Codex 로그인으로
/// <c>conversation/init</c> 을 한 번 더 부른다. 보조 조회라 실패해도 Codex 조회 결과를 해치지 않아야 한다.
/// </summary>
public sealed class CodexImageGenerationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 7, 16, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-image-tests-{Guid.NewGuid():N}");
    private readonly string _authPath;

    public CodexImageGenerationTests()
    {
        Directory.CreateDirectory(_root);
        _authPath = Path.Combine(_root, "auth.json");
        File.WriteAllText(_authPath, """{"tokens":{"access_token":"header.payload.sig-1","account_id":"acc-test"}}""");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // 2026-10-28 이 PC 계정(Plus)에서 실측한 응답 모양 그대로.
    [Fact]
    public void Parse_RealResponse_ReadsRemainingAndReset()
    {
        var allowance = Parse(InitBody(remaining: 120, resetAfter: "2026-10-29T07:16:36.853401+00:00"));

        Assert.NotNull(allowance);
        Assert.Equal(120, allowance!.Remaining);
        // 소수점 이하 마이크로초(.853401)까지 — 8,534,010 tick
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 7, 16, 36, TimeSpan.Zero).AddTicks(8_534_010), allowance.ResetAt);
    }

    [Fact]
    public void Parse_WithoutImageGen_IsNull() =>
        Assert.Null(Parse("""{"limits_progress":[{"feature_name":"deep_research","remaining":25,"reset_after":"2026-11-27T07:16:36+00:00"}]}"""));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>challenge</html>")]
    [InlineData("""{}""")]
    [InlineData("""[]""")]
    [InlineData("""{"limits_progress":null}""")]
    [InlineData("""{"limits_progress":[{"feature_name":"image_gen","remaining":null,"reset_after":"2026-10-29T07:16:36+00:00"}]}""")]
    [InlineData("""{"limits_progress":[{"feature_name":"image_gen","remaining":-1,"reset_after":"2026-10-29T07:16:36+00:00"}]}""")]
    public void Parse_UnreadableBody_IsNull(string? body) => Assert.Null(Parse(body));

    // 이미 지난 초기화 시각의 남은 횟수는 지난 창의 값이다 — 지금 남은 횟수로 보여주면 안 된다.
    [Fact]
    public void Parse_ExpiredReset_IsNull() =>
        Assert.Null(Parse(InitBody(remaining: 3, resetAfter: "2026-10-28T07:00:00+00:00")));

    // 초기화 시각만 못 읽으면 남은 횟수는 그대로 쓴다.
    [Fact]
    public void Parse_MissingReset_KeepsRemaining() =>
        Assert.Equal(new FeatureAllowance(7, null), Parse("""{"limits_progress":[{"feature_name":"image_gen","remaining":7}]}"""));

    // 둘 중 하나라도 빠지면 Cloudflare 가 403 HTML 을 준다(실측) — 계정 헤더와 User-Agent 를 반드시 싣는다.
    [Fact]
    public void CurlConfig_CarriesTheCodexHeaders_AndAnEmptyJsonPost()
    {
        var config = CodexUsageMonitor.BuildCurlConfig("header.payload.sig-1_x", "1bf9b7db-f467-4cb8-8d0f-0cc37e123fa4");

        Assert.Equal(
            "request = \"POST\"\n" +
            "data = \"{}\"\n" +
            "header = \"Content-Type: application/json\"\n" +
            "user-agent = \"codex-cli\"\n" +
            "header = \"Authorization: Bearer header.payload.sig-1_x\"\n" +
            "header = \"ChatGPT-Account-Id: 1bf9b7db-f467-4cb8-8d0f-0cc37e123fa4\"\n",
            config);
    }

    [Fact]
    public void CurlConfig_WithoutAccountId_OmitsTheHeader() =>
        Assert.DoesNotContain("ChatGPT-Account-Id", CodexUsageMonitor.BuildCurlConfig("tok", null));

    // 따옴표·줄바꿈이 섞인 값은 설정 문법을 깨고 다른 옵션을 끼워 넣을 수 있다 — 아예 부르지 않는다.
    [Theory]
    [InlineData("tok\"\nurl = \"https://example.com", "acc")]
    [InlineData("tok", "acc\noutput = \"C:\\\\x\"")]
    [InlineData("tok en", "acc")]
    [InlineData("", "acc")]
    public void CurlConfig_UnsafeValue_IsRefused(string token, string accountId) =>
        Assert.Null(CodexUsageMonitor.BuildCurlConfig(token, accountId));

    [Fact]
    public async Task Fetch_Success_ReadsInitWithTheSameLogin_AndAttachesTheAllowance()
    {
        var calls = new List<(string Token, string? AccountId)>();
        var resetAfter = DateTimeOffset.UtcNow.AddHours(20).ToString("O", CultureInfo.InvariantCulture);

        var (snapshot, status) = await FetchAsync(Ok(UsageBody()), (token, accountId) =>
        {
            calls.Add((token, accountId));
            return Task.FromResult<string?>(InitBody(remaining: 118, resetAfter));
        });

        Assert.Equal(CodexApiStatus.Succeeded, status);
        Assert.Equal(118, snapshot!.ImageGeneration!.Remaining);
        Assert.Equal(("header.payload.sig-1", "acc-test"), Assert.Single(calls));
    }

    // 보조 조회가 막혀도(Cloudflare 챌린지 → curl --fail 로 본문 없음) Codex 사용량은 그대로 성공이다.
    [Fact]
    public async Task Fetch_InitBlocked_StillSucceeded_WithoutAllowance()
    {
        var (snapshot, status) = await FetchAsync(Ok(UsageBody()), (_, _) => Task.FromResult<string?>(null));

        Assert.Equal(CodexApiStatus.Succeeded, status);
        Assert.Equal(0.42, snapshot!.ShortUsagePercent, 3);
        Assert.Null(snapshot.ImageGeneration);
    }

    [Fact]
    public async Task Fetch_InitThrows_StillSucceeded_WithoutAllowance()
    {
        var (snapshot, status) = await FetchAsync(Ok(UsageBody()), (_, _) => throw new InvalidOperationException("curl 실행 실패"));

        Assert.Equal(CodexApiStatus.Succeeded, status);
        Assert.Null(snapshot!.ImageGeneration);
    }

    // 본 조회가 실패하면 보조 조회는 부르지 않는다 — 같은 토큰으로 같은 서버에 헛걸음을 한 번 더 하지 않게.
    [Fact]
    public async Task Fetch_UsageFailed_DoesNotReadInit()
    {
        var called = false;

        var (_, status) = await FetchAsync(new HttpResponseMessage(HttpStatusCode.Unauthorized), (_, _) =>
        {
            called = true;
            return Task.FromResult<string?>(null);
        });

        Assert.Equal(CodexApiStatus.AuthFailed, status);
        Assert.False(called);
    }

    // 화면: 남은 횟수와 초기화 라벨을 쓰고, 초기화 시각이 지나면 다음 조회까지 숨긴다.
    [Fact]
    public void ViewModel_ShowsRemaining_ThenHidesAfterReset()
    {
        var vm = new CodexViewModel(new CodexUsageMonitor(), new HistoryService());

        vm.ApplyImageGeneration(new FeatureAllowance(120, Now.AddHours(24)), Now, showAbsoluteResetTime: false);

        Assert.True(vm.HasImageGeneration);
        Assert.Equal(Loc.RemainingCount(120), vm.ImageGenerationRemaining);
        // 초기화 문구는 자기 줄에 그리므로 다른 행의 " · " 구분자 없이 시작한다(#210).
        Assert.Equal(UsageCalculator.FormatResetLabel(Now.AddHours(24), false, false, Now)[" · ".Length..], vm.ImageGenerationReset);
        Assert.DoesNotContain("·", vm.ImageGenerationReset);

        vm.UpdateImageGenerationLabels(Now.AddHours(24), showAbsoluteResetTime: false);

        Assert.False(vm.HasImageGeneration);
        Assert.Equal("", vm.ImageGenerationRemaining);
        Assert.Equal("", vm.ImageGenerationReset);
    }

    [Fact]
    public void ViewModel_NoAllowance_IsHidden()
    {
        var vm = new CodexViewModel(new CodexUsageMonitor(), new HistoryService());

        vm.ApplyImageGeneration(null, Now, showAbsoluteResetTime: false);

        Assert.False(vm.HasImageGeneration);
    }

    private static FeatureAllowance? Parse(string? json) => CodexUsageMonitor.ParseImageGeneration(json, Now);

    private async Task<(ProviderUsageSnapshot? Snapshot, CodexApiStatus Status)> FetchAsync(
        HttpResponseMessage usageResponse, CodexUsageMonitor.ConversationInitReader readInit)
    {
        using var http = new HttpClient(new StubHandler(usageResponse));
        return await CodexUsageMonitor.FetchUsageFromApiAsync(http, _authPath, readInit);
    }

    private static string InitBody(int remaining, string resetAfter) => $$"""
        {
            "type": "conversation_detail_metadata",
            "banner_info": null,
            "blocked_features": [],
            "model_limits": [],
            "limits_progress": [
                { "feature_name": "deep_research", "remaining": 25, "reset_after": "2026-11-27T07:16:36.853372+00:00" },
                { "feature_name": "file_upload", "remaining": 80, "reset_after": "2026-10-28T10:16:36.853390+00:00" },
                { "feature_name": "image_gen", "remaining": {{remaining}}, "reset_after": "{{resetAfter}}" }
            ],
            "default_model_slug": "gpt-6-thinking"
        }
        """;

    private static string UsageBody() => $$"""
        {
            "plan_type": "plus",
            "rate_limit": {
                "primary_window": {
                    "used_percent": 42,
                    "limit_window_seconds": 18000,
                    "reset_at": {{DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds()}}
                }
            },
            "additional_rate_limits": null
        }
        """;

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
