using System.Net;
using System.Net.Http;
using System.Text;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>100milionseek 일일 한도 조회(#306) — 로그인·쿠키 재사용·재로그인·키 거절·형식 변경.</summary>
// 언어를 바꾸는 테스트가 있어 같은 전역(Loc)을 쓰는 다른 테스트와 나란히 돌지 않게 한다.
[Collection("WpfTests")]
public class HmsUsageServiceTests
{
    // 2026-11-19 실제 /api/me 응답의 모양(값·이메일은 바꿈).
    private const string MeJson = """
        {
          "email": "someone@example.com",
          "bonus": { "daily_tokens": 400000000, "ends_at": "2026-11-20T19:14:57.075Z" },
          "today": {
            "day": "2026-11-19", "limit": 400000000, "used": 32339025, "remaining": 367660975,
            "requests": 281, "reset_at": "2026-11-20T00:00:00.000Z", "fast_limit": 400000000, "qos": "full"
          },
          "token_reset_vouchers": 10
        }
        """;

    private const string InvalidKeyJson =
        """{"error":{"message":"That key is not valid.","type":"authentication_error","code":"invalid_api_key"}}""";
    private const string NotSignedInJson =
        """{"error":{"message":"Sign in first.","type":"authentication_error","code":"not_signed_in"}}""";

    [Fact]
    public void ParseMe_ReadsTodayAllowanceAndBonus()
    {
        var usage = HmsUsageService.ParseMe(MeJson);

        Assert.NotNull(usage);
        Assert.Equal(32_339_025, usage!.Used);
        Assert.Equal(400_000_000, usage.Limit);
        Assert.Equal(367_660_975, usage.Remaining);
        Assert.Equal(281, usage.Requests);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), usage.ResetAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 19, 14, 57, 75, TimeSpan.Zero), usage.BonusEndsAt);
        Assert.Equal(32_339_025d / 400_000_000d, usage.UsagePercent, 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"today":null}""")]
    [InlineData("""{"today":{"used":1,"reset_at":"2026-11-20T00:00:00Z"}}""")]
    [InlineData("""{"today":{"limit":0,"used":1,"reset_at":"2026-11-20T00:00:00Z"}}""")]
    [InlineData("""{"today":{"limit":10,"used":"1","reset_at":"2026-11-20T00:00:00Z"}}""")]
    [InlineData("""{"today":{"limit":10,"used":1}}""")]
    public void ParseMe_ReturnsNullWhenShapeChanged(string json)
    {
        Assert.Null(HmsUsageService.ParseMe(json));
    }

    [Fact]
    public void UsagePercent_IsClampedWhenUsedExceedsLimit()
    {
        var usage = new HmsDailyUsage(500, 400, 0, 1, DateTimeOffset.UtcNow.AddHours(1), null);
        Assert.Equal(1.0, usage.UsagePercent);
    }

    [Fact]
    public void FindSessionCookie_TakesOnlyTheSessionCookieValue()
    {
        Assert.Equal("abc.def", HmsUsageService.FindSessionCookie(
        [
            "other=1; Path=/",
            "hm_session=abc.def; Path=/; Expires=Sat, 19 Dec 2026 10:48:08 GMT; HttpOnly; Secure; SameSite=Lax",
        ]));
        Assert.Null(HmsUsageService.FindSessionCookie(["hm_session=; Path=/; Max-Age=0"]));
        Assert.Null(HmsUsageService.FindSessionCookie(["hm_sessionx=abc"]));
    }

    [Fact]
    public async Task ReadAsync_WithoutKey_DoesNotCallTheSite()
    {
        var handler = new FakeSite();
        using var service = new HmsUsageService(() => "  ", handler);

        var result = await service.ReadAsync();

        Assert.Equal(HmsReadState.NoKey, result.State);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ReadAsync_LogsInOnceAndReusesTheCookie()
    {
        var handler = new FakeSite();
        using var service = new HmsUsageService(() => "hms_key_1", handler);

        var first = await service.ReadAsync();
        var second = await service.ReadAsync();

        Assert.Equal(HmsReadState.Ok, first.State);
        Assert.Equal(HmsReadState.Ok, second.State);
        Assert.Equal(32_339_025, second.Usage!.Used);
        Assert.Equal(["POST /api/login", "GET /api/me", "GET /api/me"], handler.Requests);
        Assert.Equal("""{"api_key":"hms_key_1"}""", handler.LoginBodies.Single());
        Assert.All(handler.MeCookies, cookie => Assert.Equal("hm_session=session-1", cookie));
    }

    [Fact]
    public async Task ReadAsync_LogsInAgainWhenTheSessionExpires()
    {
        var handler = new FakeSite();
        using var service = new HmsUsageService(() => "hms_key_1", handler);
        await service.ReadAsync();

        handler.ExpireSessions();
        var result = await service.ReadAsync();

        Assert.Equal(HmsReadState.Ok, result.State);
        Assert.Equal(
            ["POST /api/login", "GET /api/me", "GET /api/me", "POST /api/login", "GET /api/me"],
            handler.Requests);
        Assert.Equal("hm_session=session-2", handler.MeCookies[^1]);
    }

    [Fact]
    public async Task ReadAsync_RejectedKeyIsNotRetriedUntilTheKeyChanges()
    {
        var handler = new FakeSite { ValidKey = "hms_good" };
        var key = "hms_bad";
        using var service = new HmsUsageService(() => key, handler);

        Assert.Equal(HmsReadState.InvalidKey, (await service.ReadAsync()).State);
        Assert.Equal(HmsReadState.InvalidKey, (await service.ReadAsync()).State);
        Assert.Equal(["POST /api/login"], handler.Requests);

        key = "hms_good";
        var result = await service.ReadAsync();

        Assert.Equal(HmsReadState.Ok, result.State);
        Assert.Equal(["POST /api/login", "POST /api/login", "GET /api/me"], handler.Requests);
    }

    [Fact]
    public async Task ReadAsync_DropsTheSessionWhenTheKeyChanges()
    {
        var handler = new FakeSite { ValidKey = null };
        var key = "hms_one";
        using var service = new HmsUsageService(() => key, handler);
        await service.ReadAsync();

        key = "hms_two";
        await service.ReadAsync();

        Assert.Equal(2, handler.LoginBodies.Count);
        Assert.Equal("""{"api_key":"hms_two"}""", handler.LoginBodies[1]);
        Assert.Equal("hm_session=session-2", handler.MeCookies[^1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task ReadAsync_ServerTroubleIsUnavailableNotInvalidKey(HttpStatusCode status)
    {
        var handler = new FakeSite { LoginStatus = status };
        using var service = new HmsUsageService(() => "hms_key_1", handler);

        Assert.Equal(HmsReadState.Unavailable, (await service.ReadAsync()).State);

        // 잠시 막힌 것뿐이라 다음 조회에 다시 로그인한다.
        handler.LoginStatus = null;
        Assert.Equal(HmsReadState.Ok, (await service.ReadAsync()).State);
    }

    [Fact]
    public async Task ReadAsync_UnauthorizedWithoutInvalidKeyCodeIsNotTreatedAsBadKey()
    {
        var handler = new FakeSite { LoginStatus = HttpStatusCode.Unauthorized, LoginBodyOverride = NotSignedInJson };
        using var service = new HmsUsageService(() => "hms_key_1", handler);

        Assert.Equal(HmsReadState.Unavailable, (await service.ReadAsync()).State);
    }

    [Fact]
    public async Task ReadAsync_NetworkFailureIsUnavailable()
    {
        var handler = new FakeSite { Throw = true };
        using var service = new HmsUsageService(() => "hms_key_1", handler);

        Assert.Equal(HmsReadState.Unavailable, (await service.ReadAsync()).State);
    }

    [Fact]
    public async Task ReadAsync_ChangedResponseShapeIsUnavailable()
    {
        var handler = new FakeSite { MeBody = """{"today":{"something":"else"}}""" };
        using var service = new HmsUsageService(() => "hms_key_1", handler);

        Assert.Equal(HmsReadState.Unavailable, (await service.ReadAsync()).State);
    }

    [Fact]
    public void BuildRow_DescribesTheDailyWindow()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var usage = HmsUsageService.ParseMe(MeJson)!;

        var row = HmsViewModel.BuildRow(usage, now, showAbsoluteResetTime: false);

        Assert.Equal("hms:daily", row.Key);
        Assert.Equal(usage.UsagePercent, row.UsagePercent);
        Assert.Equal(TimeSpan.FromDays(1), row.Window);
        Assert.True(row.HasTimeline);
        Assert.Equal(0.5, row.TimePercent, 3);
        Assert.Contains(UsageCalculator.FormatTokenShort(usage.Used), row.Summary);
        Assert.Contains(UsageCalculator.FormatTokenShort(usage.Limit), row.Summary);
        Assert.False(string.IsNullOrEmpty(row.ResetAtLabel));
    }

    [Fact]
    public void BuildRow_OmitsBonusOnceItEnded()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var withBonus = new HmsDailyUsage(1, 10, 9, 1, now.AddHours(12), now.AddHours(3));
        var ended = withBonus with { BonusEndsAt = now.AddHours(-1) };

        var bonusText = Loc.HmsBonusEnds(UsageCalculator.FormatClockStamp(now.AddHours(3), now));
        Assert.Contains(bonusText, HmsViewModel.BuildRow(withBonus, now, false).Summary);
        Assert.Equal(Loc.HmsSummary("1", "10", 1), HmsViewModel.BuildRow(ended, now, false).Summary);
    }

    [Fact]
    public async Task ReadAsync_TagsResultsWithAFingerprintOfTheKey()
    {
        var handler = new FakeSite { ValidKey = null, Throw = false };
        var key = "hms_one";
        using var service = new HmsUsageService(() => key, handler);

        var first = await service.ReadAsync();
        key = "hms_two";
        var second = await service.ReadAsync();

        Assert.Equal(HmsUsageService.KeyFingerprint("hms_one"), first.KeyId);
        Assert.Equal(HmsUsageService.KeyFingerprint("hms_two"), second.KeyId);
        Assert.NotEqual(first.KeyId, second.KeyId);
        Assert.DoesNotContain("hms_one", first.KeyId);
    }

    // 리뷰 지적(#308): 키를 B 로 바꾼 직후 조회가 실패하면 A 의 사용량이 B 의 값처럼 남아 있었다.
    [Fact]
    public void Apply_DropsUsageFromAPreviousKeyWhenTheNewKeyFails()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var usage = HmsUsageService.ParseMe(MeJson)!;
        var vm = new HmsViewModel();

        vm.Apply(new HmsReadResult(HmsReadState.Ok, usage, "key-a"), now);
        vm.Apply(new HmsReadResult(HmsReadState.Unavailable, KeyId: "key-a"), now);
        Assert.True(vm.HasQuota);
        Assert.Equal(Loc.HmsUnavailable, vm.Notice);

        vm.Apply(new HmsReadResult(HmsReadState.Unavailable, KeyId: "key-b"), now);
        Assert.False(vm.HasQuota);
        Assert.Equal(Loc.HmsUnavailable, vm.Notice);
    }

    // 리뷰 지적(#308): 안내만 있고 값이 없을 때도 섹션을 숨기면 안내를 볼 수 없다.
    // 섹션은 HasContent 로 보이고, 접힌 줄은 값 대신 "—" 를 보인다(#311).
    [Theory]
    [InlineData(HmsReadState.InvalidKey, true)]
    [InlineData(HmsReadState.Unavailable, true)]
    [InlineData(HmsReadState.NoKey, false)]
    public void NoticeAlone_KeepsTheSectionVisible(HmsReadState state, bool expected)
    {
        var vm = new HmsViewModel();
        vm.Apply(new HmsReadResult(state, KeyId: state == HmsReadState.NoKey ? null : "key-a"), DateTimeOffset.UtcNow);

        Assert.False(vm.HasQuota);
        Assert.Equal(expected, vm.HasContent);
        Assert.Equal("—", vm.PercentLabel);
    }

    [Fact]
    public void Section_ShowsPercentOnTheCollapsedRow()
    {
        var now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
        var vm = new HmsViewModel();
        vm.Apply(new HmsReadResult(HmsReadState.Ok,
            new HmsDailyUsage(100, 400, 300, 1, now.AddHours(12), null), "key-a"), now);

        Assert.True(vm.HasContent);
        Assert.Equal(0.25, vm.Percent);
        Assert.Equal(0.25.ToString("P0"), vm.PercentLabel);
    }

    [Fact]
    public void UpdateTimeProgress_DropsYesterdaysValueAfterUtcMidnight()
    {
        var now = new DateTimeOffset(2026, 11, 19, 23, 59, 0, TimeSpan.Zero);
        var vm = new HmsViewModel();
        vm.Apply(new HmsReadResult(HmsReadState.Ok,
            new HmsDailyUsage(100, 400, 300, 1, now.AddMinutes(1), null), "key-a"), now);
        Assert.True(vm.HasQuota);

        vm.UpdateTimeProgress(now.AddMinutes(2));

        Assert.False(vm.HasQuota);
    }

    [Fact]
    public void RefreshLanguage_RebuildsNoticeInTheNewLanguage()
    {
        var vm = new HmsViewModel();
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("en");
            vm.Apply(new HmsReadResult(HmsReadState.InvalidKey, KeyId: "key-a"), DateTimeOffset.UtcNow);
            var english = vm.Notice;

            Loc.SetLanguage("ko");
            vm.RefreshLanguage();

            Assert.Equal(Loc.HmsInvalidKey, vm.Notice);
            Assert.NotEqual(english, vm.Notice);
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    /// <summary>대시보드 내부 API 를 흉내 낸다 — 로그인마다 새 세션 쿠키, 만료시키면 /api/me 가 401.</summary>
    private sealed class FakeSite : HttpMessageHandler
    {
        private readonly HashSet<string> _liveSessions = [];
        private int _sessionCounter;

        public string? ValidKey { get; set; } = "hms_key_1";
        public HttpStatusCode? LoginStatus { get; set; }
        public string? LoginBodyOverride { get; set; }
        public string MeBody { get; set; } = MeJson;
        public bool Throw { get; set; }
        public List<string> Requests { get; } = [];
        public List<string> LoginBodies { get; } = [];
        public List<string> MeCookies { get; } = [];

        public void ExpireSessions() => _liveSessions.Clear();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Throw) throw new HttpRequestException("offline");
            Requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            Assert.Equal("100milionseek.top", request.RequestUri.Host);

            if (request.RequestUri.AbsolutePath == "/api/login")
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                LoginBodies.Add(body);
                if (LoginStatus is { } status)
                    return Json(status, LoginBodyOverride ?? """{"error":{"code":"busy"}}""");
                if (ValidKey != null && body != $$"""{"api_key":"{{ValidKey}}"}""")
                    return Json(HttpStatusCode.Unauthorized, InvalidKeyJson);
                var session = $"session-{++_sessionCounter}";
                _liveSessions.Add(session);
                var ok = Json(HttpStatusCode.OK, """{"ok":true}""");
                ok.Headers.Add("Set-Cookie", $"hm_session={session}; Path=/; HttpOnly; Secure; SameSite=Lax");
                return ok;
            }

            if (request.RequestUri.AbsolutePath == "/api/me")
            {
                var cookie = request.Headers.TryGetValues("Cookie", out var values) ? values.Single() : "";
                MeCookies.Add(cookie);
                var live = cookie.StartsWith("hm_session=", StringComparison.Ordinal)
                    && _liveSessions.Contains(cookie["hm_session=".Length..]);
                return live ? Json(HttpStatusCode.OK, MeBody) : Json(HttpStatusCode.Unauthorized, NotSignedInJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

/// <summary>
/// 실제 사이트에 붙는 확인 — 키가 환경 변수 <c>HMS_API_KEY</c> 에 있을 때만 돈다(CI 에는 없다).
/// .NET HttpClient 가 Cloudflare 에 막히지 않는지가 핵심이다(curl 만으로는 알 수 없다).
/// </summary>
[Trait("Category", "Network")]
public class HmsUsageLiveTests
{
    [Fact]
    public async Task RealSite_ReturnsTodayAllowance()
    {
        var key = Environment.GetEnvironmentVariable("HMS_API_KEY");
        if (string.IsNullOrWhiteSpace(key)) return;

        using var service = new HmsUsageService(() => key);
        var result = await service.ReadAsync();

        Assert.Equal(HmsReadState.Ok, result.State);
        Assert.True(result.Usage!.Limit > 0);
        Assert.True(result.Usage.ResetAt > DateTimeOffset.UtcNow);
    }
}
