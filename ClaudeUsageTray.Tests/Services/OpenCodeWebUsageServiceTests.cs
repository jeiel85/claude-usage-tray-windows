using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using System.IO;
using System.Text.Json;
using System.Windows;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public sealed class OpenCodeWebUsageServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 9, 0, 0, TimeSpan.FromHours(9));

    /// <summary>
    /// 새 콘솔 <c>GET /console/api/go/status</c> 의 모양(콘솔 공개 번들의 스키마 기준, 값은 합성).
    /// 마이크로센트는 문자열(bigint), 시각은 ISO-8601 UTC 문자열이다.
    /// </summary>
    private static string GoStatusJson(
        string fiveHourResetsAt = "\"2026-09-20T03:00:00.000Z\"",
        string fiveHourUsed = "300000000",
        string monthMeter = "\"month\":{\"resetsAt\":\"2026-10-19T00:00:00.000Z\",\"limitMicroCents\":\"6000000000\",\"usedMicroCents\":\"600000000\"}") =>
        "{\"subscriberUserId\":\"user_TEST\",\"product\":\"go\",\"renewalProduct\":\"go\",\"useBalance\":false," +
        "\"cancelAtPeriodEnd\":false,\"renewalPending\":false,\"access\":{\"startsAt\":\"2026-09-19T00:00:00.000Z\"," +
        "\"endsAt\":\"2026-10-19T00:00:00.000Z\",\"cancelAtPeriodEnd\":false,\"meters\":{" +
        $"\"fiveHour\":{{\"startsAt\":null,\"resetsAt\":{fiveHourResetsAt},\"limitMicroCents\":\"1200000000\",\"usedMicroCents\":\"{fiveHourUsed}\"}}," +
        "\"week\":{\"startsAt\":\"2026-09-14T00:00:00.000Z\",\"resetsAt\":\"2026-09-21T00:00:00.000Z\",\"limitMicroCents\":\"3000000000\",\"usedMicroCents\":\"1200000000\"}," +
        $"{monthMeter}}}}}}}";

    private const string OrgsJson =
        """[{"id":"org_PERSONAL","name":"Personal","avatarUrl":null},{"id":"wrk_TEAM123","name":"Team","avatarUrl":null}]""";

    [Fact]
    public void ParseGoStatus_ReadsConsoleMetersAsUsedOverLimit()
    {
        var usage = OpenCodeWebUsageService.ParseGoStatus(GoStatusJson(), Now);

        Assert.NotNull(usage);
        Assert.Equal(0.25, usage.Rolling.UsagePercent, 6);
        Assert.Equal(0.40, usage.Weekly.UsagePercent, 6);
        Assert.Equal(0.10, usage.Monthly.UsagePercent, 6);
        Assert.Equal(Now.ToUniversalTime(), usage.ObservedAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 3, 0, 0, TimeSpan.Zero), usage.Rolling.ResetAt);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero), usage.Weekly.ResetAt);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), usage.Monthly.ResetAt);
        // 표시 문자열이 시간대 변환을 빼먹어도 같은 시각이 되도록 로컬 오프셋으로 바꿔 둔다.
        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(usage.Weekly.ResetAt), usage.Weekly.ResetAt.Offset);
    }

    // 롤링 창은 첫 사용부터 시작해, 시작 전이면 초기화 시각이 비어 온다. 레거시 콘솔이 같은 상황에
    // 돌려주던 값(0%, 지금부터 5시간 뒤)으로 맞춰야 캐시·동기화의 "리셋이 지난 값은 버린다" 검사를 통과한다.
    [Fact]
    public void ParseGoStatus_RollingWindowNotStarted_MatchesLegacyZeroAndFiveHours()
    {
        var usage = OpenCodeWebUsageService.ParseGoStatus(GoStatusJson(fiveHourResetsAt: "null", fiveHourUsed: "0"), Now);

        Assert.NotNull(usage);
        Assert.Equal(0, usage.Rolling.UsagePercent);
        Assert.Equal(Now.AddHours(5), usage.Rolling.ResetAt);
        Assert.True(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, Now.AddMinutes(10)));
    }

    [Fact]
    public void ParseGoStatus_MonthWithoutReset_UsesSubscriptionPeriodEnd()
    {
        var usage = OpenCodeWebUsageService.ParseGoStatus(
            GoStatusJson(monthMeter: "\"month\":{\"limitMicroCents\":\"6000000000\",\"usedMicroCents\":\"600000000\"}"), Now);

        Assert.NotNull(usage);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), usage.Monthly.ResetAt);
    }

    [Fact]
    public void ParseGoStatus_ClampsUsageBeyondTheLimitToFull()
    {
        var usage = OpenCodeWebUsageService.ParseGoStatus(GoStatusJson(fiveHourUsed: "1500000000"), Now);

        Assert.NotNull(usage);
        Assert.Equal(1.0, usage.Rolling.UsagePercent);
    }

    [Theory]
    [InlineData("null")]                                       // 구독 없음
    [InlineData("{\"product\":\"go\",\"access\":null}")]          // 일시정지·결제 대기
    [InlineData("{\"access\":{\"meters\":{}}}")]                  // 창이 빠짐
    [InlineData("<!DOCTYPE html><html></html>")]               // JSON 이 아님
    [InlineData("")]
    public void ParseGoStatus_ReturnsNullWithoutReadableMeters(string body)
    {
        Assert.Null(OpenCodeWebUsageService.ParseGoStatus(body, Now));
    }

    [Theory]
    [InlineData("\"limitMicroCents\":\"0\",\"usedMicroCents\":\"0\"")]
    [InlineData("\"limitMicroCents\":\"100\",\"usedMicroCents\":\"-1\"")]
    [InlineData("\"limitMicroCents\":\"abc\",\"usedMicroCents\":\"1\"")]
    public void ParseGoStatus_RejectsInvalidMeterValues(string values)
    {
        var json = GoStatusJson(monthMeter: $"\"month\":{{\"resetsAt\":\"2026-10-19T00:00:00.000Z\",{values}}}");

        Assert.Null(OpenCodeWebUsageService.ParseGoStatus(json, Now));
        Assert.True(OpenCodeWebUsageService.HasGoAccess(json));
    }

    [Fact]
    public void ParseOrgIds_KeepsOnlyValidDistinctWorkspaceIds()
    {
        var ids = OpenCodeWebUsageService.ParseOrgIds(
            """[{"id":"org_A1"},{"id":"wrk_B2"},{"id":"org_A1"},{"id":"usr_X"},{"id":"org_bad id"},{"name":"no id"},"org_C3"]""");

        Assert.Equal(new[] { "org_A1", "wrk_B2" }, ids);
        Assert.Empty(OpenCodeWebUsageService.ParseOrgIds("{\"_tag\":\"Unauthorized\"}"));
        Assert.Empty(OpenCodeWebUsageService.ParseOrgIds("not json"));
    }

    // 할당량은 저장된 작업공간의 go/status 한 번으로 읽고, 작업공간 목록은 다시 받지 않는다.
    // 뒤따르는 건 크레딧을 볼 수 있는지 확인하는 역할 조회뿐이다(멤버면 결제 정보는 묻지 않는다).
    [Fact]
    public async Task ReadQuota_UsesSavedWorkspaceWithoutListingWorkspaces()
    {
        var fetch = new FakeConsole { [("/go/status", "org_PERSONAL")] = Ok(GoStatusJson()) };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.Success, outcome.Kind);
        Assert.Equal("org_PERSONAL", outcome.OrgId);
        Assert.Equal(0.25, outcome.Usage!.Rolling.UsagePercent, 6);
        Assert.Equal(
            new (string, string?)[] { ("/go/status", "org_PERSONAL"), ("/orgs/current", "org_PERSONAL") },
            fetch.Requests);
    }

    [Fact]
    public async Task ReadQuota_FindsTheWorkspaceWithGoAmongSeveral()
    {
        var fetch = new FakeConsole
        {
            [("/orgs", null)] = Ok(OrgsJson),
            [("/go/status", "org_PERSONAL")] = Ok("null"),
            [("/go/status", "wrk_TEAM123")] = Ok(GoStatusJson()),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, null, Now);

        Assert.Equal(ConsoleReadKind.Success, outcome.Kind);
        Assert.Equal("wrk_TEAM123", outcome.OrgId);
    }

    // 저장된 작업공간이 Go 를 잃었으면(해지 등) 목록에서 다시 찾되, 이미 확인한 곳은 다시 묻지 않는다.
    [Fact]
    public async Task ReadQuota_RediscoversWhenTheSavedWorkspaceLostGo()
    {
        var fetch = new FakeConsole
        {
            [("/go/status", "org_PERSONAL")] = Ok("null"),
            [("/orgs", null)] = Ok(OrgsJson),
            [("/go/status", "wrk_TEAM123")] = Ok(GoStatusJson()),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, "org_PERSONAL", Now);

        Assert.Equal("wrk_TEAM123", outcome.OrgId);
        Assert.Single(fetch.Requests, request => request == ("/go/status", "org_PERSONAL"));
    }

    // 로그아웃 판정은 작업공간 헤더 없이 부르는 작업공간 목록의 401 로만 한다.
    [Theory]
    [InlineData("org_PERSONAL")]
    [InlineData(null)]
    public async Task ReadQuota_TreatsUnauthorizedWorkspaceListAsSignedOut(string? savedOrgId)
    {
        var fetch = new FakeConsole { Fallback = new ConsoleResponse(401, "") };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, savedOrgId, Now);

        Assert.Equal(ConsoleReadKind.SignedOut, outcome.Kind);
    }

    // 저장된 작업공간만 401 이면 그 작업공간에서 빠졌거나 다른 계정으로 로그인한 것일 수 있다 —
    // 세션은 살아 있으므로 목록에서 다시 찾고, 이미 거절당한 작업공간은 다시 묻지 않는다.
    [Fact]
    public async Task ReadQuota_DoesNotTreatAWorkspaceScopedUnauthorizedAsSignOut()
    {
        var fetch = new FakeConsole
        {
            [("/go/status", "org_PERSONAL")] = new ConsoleResponse(401, ""),
            [("/orgs", null)] = Ok(OrgsJson),
            [("/go/status", "wrk_TEAM123")] = Ok(GoStatusJson()),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.Success, outcome.Kind);
        Assert.Equal("wrk_TEAM123", outcome.OrgId);
        Assert.Single(fetch.Requests, request => request == ("/go/status", "org_PERSONAL"));
    }

    [Fact]
    public async Task ReadQuota_ReportsAWorkspaceScopedUnauthorizedDuringDiscoveryAsFailure()
    {
        var fetch = new FakeConsole
        {
            [("/orgs", null)] = Ok("[{\"id\":\"org_PERSONAL\"}]"),
            [("/go/status", "org_PERSONAL")] = new ConsoleResponse(401, ""),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, null, Now);

        Assert.Equal(ConsoleReadKind.Failed, outcome.Kind);
    }

    // 저장된 작업공간의 일시 오류는 목록을 받은 뒤 한 번 더 묻는다.
    [Fact]
    public async Task ReadQuota_RetriesTheSavedWorkspaceAfterATransientFailure()
    {
        var calls = 0;
        var fetch = new FakeConsole { [("/orgs", null)] = Ok(OrgsJson) };
        Task<ConsoleResponse> FetchAsync(string path, string? orgId)
        {
            if (path == "/go/status" && orgId == "org_PERSONAL")
                return Task.FromResult(++calls == 1 ? new ConsoleResponse(0, "") : Ok(GoStatusJson()));
            return fetch.FetchAsync(path, orgId);
        }

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.Success, outcome.Kind);
        Assert.Equal("org_PERSONAL", outcome.OrgId);
        Assert.Equal(2, calls);
    }

    // 응답을 못 받은 것(부팅 직후 네트워크 미준비 등)은 로그아웃이 아니다(#130).
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(403)]
    public async Task ReadQuota_TreatsMissingOrUnexpectedResponsesAsFailureNotSignOut(int status)
    {
        var fetch = new FakeConsole { Fallback = new ConsoleResponse(status, "") };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.Failed, outcome.Kind);
        Assert.False(string.IsNullOrEmpty(outcome.Error));
    }

    [Fact]
    public async Task ReadQuota_ReportsNoQuotaWhenNoWorkspaceHasGo()
    {
        var fetch = new FakeConsole
        {
            [("/orgs", null)] = Ok(OrgsJson),
            [("/go/status", "org_PERSONAL")] = Ok("null"),
            // 해지된 채 기간이 끝난 구독 — 콘솔도 Go 카드를 감추므로 미구독과 같이 본다.
            [("/go/status", "wrk_TEAM123")] = Ok("{\"product\":\"go\",\"cancelAtPeriodEnd\":true,\"access\":null}"),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, null, Now);

        Assert.Equal(ConsoleReadKind.NoQuota, outcome.Kind);
        Assert.Equal(Loc.OpenCodeWebNoGoQuota, outcome.Error);
        Assert.Null(outcome.Inactive);
    }

    // 구독은 있는데 지금 못 쓰는 상태면 "Go 를 구독한 계정으로 로그인" 대신 그 이유를 알린다.
    [Fact]
    public async Task ReadQuota_ReportsWhyAnInactiveSubscriptionHasNoQuota()
    {
        var fetch = new FakeConsole
        {
            [("/go/status", "org_PERSONAL")] = Ok(
                "{\"product\":\"go\",\"renewalProduct\":\"go\",\"useBalance\":false,\"cancelAtPeriodEnd\":false," +
                "\"renewalPending\":true,\"access\":null}"),
            [("/orgs", null)] = Ok("[{\"id\":\"org_PERSONAL\"}]"),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.NoQuota, outcome.Kind);
        Assert.Equal(OpenCodeGoSubscriptionStatus.RenewalProcessing, outcome.Inactive);
        Assert.Equal(Loc.OpenCodeGoInactive(OpenCodeGoSubscriptionStatus.RenewalProcessing), outcome.Error);
    }

    // 콘솔 Go 카드의 상태 칩과 같은 순서로 판정한다(필드명은 콘솔 번들 그대로, renewalPaymentAttemptID 의 대문자 ID 포함).
    [Theory]
    [InlineData("\"renewalPaymentAttemptID\":\"pay_1\",\"renewalPending\":true", OpenCodeGoSubscriptionStatus.ApprovalNeeded)]
    [InlineData("\"renewalPending\":true,\"renewalAuthorizationRequired\":true", OpenCodeGoSubscriptionStatus.RenewalProcessing)]
    [InlineData("\"renewalAuthorizationRequired\":true", OpenCodeGoSubscriptionStatus.ReauthorizationNeeded)]
    [InlineData("\"renewalRetryAt\":\"2026-09-21T00:00:00.000Z\"", OpenCodeGoSubscriptionStatus.Paused)]
    [InlineData("\"renewalPaymentAttemptID\":null,\"renewalPending\":false", OpenCodeGoSubscriptionStatus.Paused)]
    public void ParseInactiveStatus_FollowsTheConsoleOrder(string fields, OpenCodeGoSubscriptionStatus expected)
    {
        var json = $"{{\"product\":\"go\",\"cancelAtPeriodEnd\":false,{fields},\"access\":null}}";

        Assert.Equal(expected, OpenCodeWebUsageService.ParseInactiveStatus(json));
    }

    [Theory]
    [InlineData("null")]                                                // 구독 없음
    [InlineData("{\"cancelAtPeriodEnd\":true,\"access\":null}")]        // 해지된 채 기간 종료
    [InlineData("{\"access\":{\"meters\":{}}}")]                        // 쓸 수 있는 상태
    [InlineData("{\"product\":\"go\"}")]                                // access 자체가 없음 — 단정하지 않는다
    [InlineData("not json")]
    public void ParseInactiveStatus_IsNullUnlessASubscriptionIsHeldWithoutAccess(string json)
    {
        Assert.Null(OpenCodeWebUsageService.ParseInactiveStatus(json));
    }

    [Fact]
    public void ParseGoStatus_ReadsPlanExtraUsageAndPeriodEnd()
    {
        var json = GoStatusJson()
            .Replace("\"product\":\"go\"", "\"product\":\"go-plus\"")
            .Replace("\"renewalProduct\":\"go\"", "\"renewalProduct\":\"go-plus\"")
            .Replace("\"useBalance\":false", "\"useBalance\":true");

        var usage = OpenCodeWebUsageService.ParseGoStatus(json, Now);

        Assert.NotNull(usage);
        Assert.Equal("go-plus", usage.Product);
        Assert.True(usage.ExtraUsageEnabled);
        Assert.Null(usage.CreditsUsd);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero), usage.PeriodEndsAt);
        Assert.Equal(OpenCodeGoSubscriptionStatus.Renews, usage.Subscription);
    }

    [Theory]
    [InlineData("\"renewalRetryAt\":\"2026-09-21T00:00:00.000Z\",\"cancelAtPeriodEnd\":true", "go", OpenCodeGoSubscriptionStatus.PaymentFailed)]
    [InlineData("\"renewalAuthorizationRequired\":true,\"cancelAtPeriodEnd\":true", "go", OpenCodeGoSubscriptionStatus.ActionNeeded)]
    [InlineData("\"cancelAtPeriodEnd\":true", "go-plus", OpenCodeGoSubscriptionStatus.Ends)]
    [InlineData("\"renewalRetryAt\":null", "go-plus", OpenCodeGoSubscriptionStatus.SwitchesToGo)]
    [InlineData("\"renewalRetryAt\":null", "go", OpenCodeGoSubscriptionStatus.Renews)]
    public void ParseGoStatus_ReadsTheSubscriptionStatusInConsoleOrder(
        string fields, string product, OpenCodeGoSubscriptionStatus expected)
    {
        // renewalProduct 는 항상 "go" — Go Plus 이면서 갱신 상품이 Go 면 기간 끝에 Go 로 내려간다.
        var json = GoStatusJson()
            .Replace("\"product\":\"go\"", $"\"product\":\"{product}\"")
            .Replace("\"cancelAtPeriodEnd\":false,\"renewalPending\"", $"{fields},\"renewalPending\"");

        Assert.Equal(expected, OpenCodeWebUsageService.ParseGoStatus(json, Now)?.Subscription);
    }

    private static OpenCodeWebUsage ExtraUsageQuota(bool useBalance) => new()
    {
        ObservedAtUtc = Now,
        Product = "go",
        ExtraUsageEnabled = useBalance,
    };

    // 멤버는 콘솔처럼 설정만 보고 추가 사용을 판정하고, 결제 정보는 묻지 않는다(콘솔도 관리자에게만 보여준다).
    [Theory]
    [InlineData("{\"role\":\"member\"}", 200)]
    [InlineData("", 500)]   // 역할을 모르면 콘솔처럼 멤버로 본다
    public async Task ApplyBilling_ForMembers_KeepsTheSettingAndSkipsCredits(string body, int status)
    {
        var fetch = new FakeConsole { [("/orgs/current", "org_PERSONAL")] = new ConsoleResponse(status, body) };

        var usage = await OpenCodeWebUsageService.ApplyBillingAsync(fetch.FetchAsync, "org_PERSONAL", ExtraUsageQuota(true));

        Assert.True(usage.ExtraUsageEnabled);
        Assert.Null(usage.CreditsUsd);
        Assert.DoesNotContain(fetch.Requests, request => request.Path == "/billing/status");
    }

    // 관리자에게는 크레딧이 남아 있을 때만 추가 사용을 켜짐으로 본다. 크레딧은 결제 화면의 Available credits 값.
    [Theory]
    [InlineData("owner", true, "\"1234000000\"", true, 12.34)]
    [InlineData("admin", true, "\"0\"", false, 0)]
    [InlineData("admin", false, "\"500000000\"", false, 5)]
    public async Task ApplyBilling_ForAdmins_ReadsAvailableCredits(
        string role, bool useBalance, string available, bool expectedExtra, double expectedUsd)
    {
        var fetch = new FakeConsole
        {
            [("/orgs/current", "org_PERSONAL")] = Ok($"{{\"userId\":\"user_TEST\",\"role\":\"{role}\"}}"),
            [("/billing/status", "org_PERSONAL")] = Ok(
                $"{{\"billingMode\":\"prepaid\",\"mode\":\"pay-as-you-go\",\"balanceMicroCents\":\"999\",\"availableMicroCents\":{available}}}"),
        };

        var usage = await OpenCodeWebUsageService.ApplyBillingAsync(fetch.FetchAsync, "org_PERSONAL", ExtraUsageQuota(useBalance));

        Assert.Equal(expectedExtra, usage.ExtraUsageEnabled);
        Assert.Equal((decimal)expectedUsd, usage.CreditsUsd);
    }

    // 관리자인데 결제 정보를 못 읽으면 콘솔처럼 크레딧 0 으로 보고 추가 사용도 끈다. 401 이어도 로그아웃이 아니다.
    [Theory]
    [InlineData(403)]
    [InlineData(401)]
    [InlineData(0)]
    public async Task ApplyBilling_WhenCreditsCannotBeRead_TurnsExtraUsageOffWithoutCredits(int status)
    {
        var fetch = new FakeConsole
        {
            [("/orgs/current", "org_PERSONAL")] = Ok("{\"role\":\"admin\"}"),
            [("/billing/status", "org_PERSONAL")] = new ConsoleResponse(status, ""),
        };

        var usage = await OpenCodeWebUsageService.ApplyBillingAsync(fetch.FetchAsync, "org_PERSONAL", ExtraUsageQuota(true));

        Assert.False(usage.ExtraUsageEnabled);
        Assert.Null(usage.CreditsUsd);
    }

    // 자동 조회의 제한 시간이 역할·크레딧 조회 중에 끝나도 이미 읽은 할당량은 그대로 돌려준다.
    [Fact]
    public async Task ReadQuota_KeepsTheQuotaWhenTheTimeLimitEndsDuringTheCreditLookup()
    {
        var fetch = new FakeConsole { [("/go/status", "org_PERSONAL")] = Ok(GoStatusJson()) };
        Task<ConsoleResponse> FetchAsync(string path, string? orgId) =>
            path == "/go/status"
                ? fetch.FetchAsync(path, orgId)
                : Task.FromCanceled<ConsoleResponse>(new CancellationToken(canceled: true));

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.Success, outcome.Kind);
        Assert.Equal(0.25, outcome.Usage!.Rolling.UsagePercent, 6);
        Assert.Null(outcome.Usage.CreditsUsd);
    }

    // 성공한 조회에는 관리자의 크레딧과 그에 따른 추가 사용 판정이 붙어 나온다.
    [Fact]
    public async Task ReadQuota_AttachesCreditsToASuccessfulRead()
    {
        var fetch = new FakeConsole
        {
            [("/go/status", "org_PERSONAL")] = Ok(GoStatusJson().Replace("\"useBalance\":false", "\"useBalance\":true")),
            [("/orgs/current", "org_PERSONAL")] = Ok("{\"role\":\"owner\"}"),
            [("/billing/status", "org_PERSONAL")] = Ok("{\"availableMicroCents\":\"250000000\"}"),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, "org_PERSONAL", Now);

        Assert.Equal(ConsoleReadKind.Success, outcome.Kind);
        Assert.True(outcome.Usage!.ExtraUsageEnabled);
        Assert.Equal(2.5m, outcome.Usage.CreditsUsd);
    }

    // 접근 정보는 있는데 창을 못 읽었으면 콘솔 응답 형식이 바뀐 것이다 — "미구독" 으로 덮지 않는다.
    [Fact]
    public async Task ReadQuota_DistinguishesAChangedResponseShapeFromNoSubscription()
    {
        var fetch = new FakeConsole
        {
            [("/orgs", null)] = Ok("[{\"id\":\"org_PERSONAL\"}]"),
            [("/go/status", "org_PERSONAL")] = Ok("{\"access\":{\"meters\":{\"rolling\":{}}}}"),
        };

        var outcome = await OpenCodeWebUsageService.ReadQuotaAsync(fetch.FetchAsync, null, Now);

        Assert.Equal(ConsoleReadKind.Failed, outcome.Kind);
        Assert.Equal(Loc.OpenCodeWebQuotaNotFound("/go/status"), outcome.Error);
    }

    [Fact]
    public void ParseFetchMessage_ReadsOnlyWellFormedReplies()
    {
        Assert.Equal(("abc", new ConsoleResponse(200, "[]")),
            OpenCodeWebUsageService.ParseFetchMessage("{\"id\":\"abc\",\"status\":200,\"body\":\"[]\"}"));
        Assert.Equal(("abc", new ConsoleResponse(401, "")),
            OpenCodeWebUsageService.ParseFetchMessage("{\"id\":\"abc\",\"status\":401}"));
        Assert.Null(OpenCodeWebUsageService.ParseFetchMessage("{\"id\":\"abc\",\"status\":\"200\"}"));
        Assert.Null(OpenCodeWebUsageService.ParseFetchMessage("{\"status\":200}"));
        Assert.Null(OpenCodeWebUsageService.ParseFetchMessage("hello"));
        Assert.Null(OpenCodeWebUsageService.ParseFetchMessage(null));
    }

    [Fact]
    public void BuildFetchScript_EmbedsValuesAsJsonAndSendsWorkspaceHeaderOnlyWhenScoped()
    {
        var scoped = OpenCodeWebUsageService.BuildFetchScript(
            "id\"1", "https://opencode.ai/console/api/go/status", "org_PERSONAL");
        var unscoped = OpenCodeWebUsageService.BuildFetchScript(
            "id2", "https://opencode.ai/console/api/orgs", null);

        Assert.Contains(JsonSerializer.Serialize("id\"1"), scoped);
        Assert.Contains("\"x-org-id\":\"org_PERSONAL\"", scoped);
        Assert.Contains("credentials: \"same-origin\"", scoped);
        Assert.DoesNotContain("x-org-id", unscoped);
        Assert.Contains("\"https://opencode.ai/console/api/orgs\"", unscoped);
    }

    [Theory]
    [InlineData("https://opencode.ai/console/api/orgs", true)]
    [InlineData("https://opencode.ai/console/login?next=%2Fgo", true)]
    [InlineData("http://opencode.ai/console/api/orgs", false)]
    [InlineData("https://opencode.ai:8443/console", false)]
    [InlineData("https://auth.opencode.ai/authorize", false)]
    [InlineData("https://github.com/login/oauth/authorize", false)]
    [InlineData("about:blank", false)]
    [InlineData(null, false)]
    public void IsConsoleOrigin_AllowsOnlyTheOpenCodeSite(string? source, bool expected)
    {
        Assert.Equal(expected, OpenCodeWebUsageService.IsConsoleOrigin(source));
    }

    [Fact]
    public void CachedFallback_RemainsAvailableDuringRetryButNotPastItsSafetyBounds()
    {
        var observedAt = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.FromHours(9));
        var usage = new ClaudeUsageTray.Models.OpenCodeWebUsage
        {
            ObservedAtUtc = observedAt,
            Rolling = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddHours(5) },
            Weekly = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddDays(7) },
            Monthly = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddMonths(1) },
        };

        Assert.True(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, observedAt.AddMinutes(39)));
        Assert.False(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, observedAt.AddMinutes(41)));

        var resetExpired = new ClaudeUsageTray.Models.OpenCodeWebUsage
        {
            ObservedAtUtc = observedAt,
            Rolling = new ClaudeUsageTray.Models.OpenCodeQuotaWindow { ResetAt = observedAt.AddMinutes(10) },
        };
        Assert.False(OpenCodeWebUsageService.IsCachedFallbackUsable(resetExpired, observedAt.AddMinutes(11)));
        Assert.False(OpenCodeWebUsageService.IsCachedFallbackUsable(usage, observedAt.AddMinutes(-6)));
    }

    [Fact]
    public void CenterWithinWorkArea_UsesExplicitCoordinatesOnNegativeMonitor()
    {
        var bounds = OpenCodeWebUsageService.CenterWithinWorkArea(
            new Rect(-1920, 0, 1920, 1040), new System.Windows.Size(920, 720));

        Assert.Equal(-1420, bounds.Left);
        Assert.Equal(160, bounds.Top);
        Assert.Equal(920, bounds.Width);
        Assert.Equal(720, bounds.Height);
    }

    [Fact]
    public void CenterWithinWorkArea_ClampsWindowToSmallWorkArea()
    {
        var bounds = OpenCodeWebUsageService.CenterWithinWorkArea(
            new Rect(100, 50, 800, 600), new System.Windows.Size(920, 720));

        Assert.Equal(new Rect(100, 50, 800, 600), bounds);
    }

    [Fact]
    public void SavedWorkspace_PersistsAcrossServiceRestarts()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"opencode-org-{Guid.NewGuid():N}");
        try
        {
            using (var first = new OpenCodeWebUsageService(dataRoot))
            {
                Assert.Null(first.SavedOrgId);
                Assert.False(first.HasSavedSession);
                Assert.True(OpenCodeWebUsageService.WriteOrgId(Path.Combine(dataRoot, "console-org.txt"), "org_TEST123"));
            }

            using var restarted = new OpenCodeWebUsageService(dataRoot);
            Assert.Equal("org_TEST123", restarted.SavedOrgId);
            Assert.True(restarted.HasSavedSession);
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
        }
    }

    // 레거시 콘솔 시절의 경로 파일은 새 콘솔 세션과 무관하다 — 남아 있어도 "여기서 된 적 있다" 로 치지 않는다.
    [Fact]
    public void HasSavedSession_IgnoresTheLegacyWorkspaceRouteAndInvalidIds()
    {
        var dataRoot = Path.Combine(Path.GetTempPath(), $"opencode-org-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(dataRoot);
            File.WriteAllText(Path.Combine(dataRoot, "workspace-route.txt"), "https://opencode.ai/workspace/wrk_OLD123/go");
            using (var legacyOnly = new OpenCodeWebUsageService(dataRoot))
                Assert.False(legacyOnly.HasSavedSession);

            File.WriteAllText(Path.Combine(dataRoot, "console-org.txt"), "https://opencode.ai/console/go");
            using var invalid = new OpenCodeWebUsageService(dataRoot);
            Assert.False(invalid.HasSavedSession);
            Assert.False(OpenCodeWebUsageService.WriteOrgId(Path.Combine(dataRoot, "console-org.txt"), "../escape"));
        }
        finally
        {
            if (Directory.Exists(dataRoot)) Directory.Delete(dataRoot, true);
        }
    }

    // 부팅 직후 네트워크 미준비처럼 확인 자체를 못 한 실패는 곧 회복되므로 짧게 쉬었다가 다시 본다.
    // 30분을 쉬면 멀쩡한 세션에 로그인 버튼이 그만큼 붙어 있게 된다.
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(9, 16)]
    public void RetryDelay_ForUnavailable_BacksOffGraduallyAndStaysShorterThanSignedOut(
        int consecutiveFailures, int expectedMinutes)
    {
        var delay = OpenCodeWebUsageService.RetryDelayFor(
            ClaudeUsageTray.Models.OpenCodeWebSessionState.Unavailable, consecutiveFailures);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
        Assert.True(delay < OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.SignedOut, 1));
    }

    // 로그인이 풀린 상태는 사용자가 로그인하기 전엔 결과가 달라지지 않으므로 종전대로 길게 쉰다.
    [Fact]
    public void RetryDelay_ForSignedOut_StaysLong()
    {
        Assert.Equal(TimeSpan.FromMinutes(30),
            OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.SignedOut, 1));
        Assert.Equal(TimeSpan.FromMinutes(30),
            OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.SignedOut, 9));
    }

    // Go 할당량이 없는 계정도 사용자가 구독하거나 계정을 바꾸기 전엔 결과가 같으므로 길게 쉰다.
    [Fact]
    public void RetryDelay_ForNoQuota_StaysLong()
    {
        Assert.Equal(TimeSpan.FromMinutes(30),
            OpenCodeWebUsageService.RetryDelayFor(ClaudeUsageTray.Models.OpenCodeWebSessionState.NoQuota, 1));
    }

    private static ConsoleResponse Ok(string body) => new(200, body);

    /// <summary>경로·작업공간별로 정해 둔 응답을 돌려주고, 받은 요청을 순서대로 기록한다.</summary>
    private sealed class FakeConsole : Dictionary<(string Path, string? OrgId), ConsoleResponse>
    {
        public List<(string Path, string? OrgId)> Requests { get; } = [];
        public ConsoleResponse Fallback { get; init; } = new(404, "");

        public Task<ConsoleResponse> FetchAsync(string path, string? orgId)
        {
            Requests.Add((path, orgId));
            return Task.FromResult(TryGetValue((path, orgId), out var response) ? response : Fallback);
        }
    }
}
