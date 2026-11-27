using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// Console 프로모션 크레딧(#354). 응답 모양은 2026-11-27 platform.claude.com 실측(금액·ID 는 그대로, 조직 UUID 는 바꿈).
/// Loc 을 바꾸므로 Loc 을 쓰는 다른 테스트와 같은 컬렉션에서 돈다.
/// </summary>
[Collection("WpfTests")]
public class ClaudeConsoleCreditServiceTests
{
    private const string ApiOrg = "11111111-2222-3333-4444-555555555555";
    private const string ChatOrg = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.FromHours(9));

    private const string CreditsJson = """
        {"amount":5781,"currency":"USD","balance":{"money":null,"credits":{"amount_minor":5781,"exponent":2}},
         "balance_credits":57,"amount_without_scoped_credits":0,"tranches":[],
         "promo_tranches":[{"currency":"USD","expires_at":"2026-12-10T00:00:00Z",
           "granted":{"credits":{"amount_minor":10000,"exponent":2},"money":null},
           "granted_amount_minor_units":10000,"granted_at":"2026-11-07T03:15:33.865000Z",
           "id":"4d50e128-5fe4-4af7-90ad-889d56629648","name":"API credit (Max)","program_id":"subscription_api_credit",
           "remaining":{"credits":{"amount_minor":5780,"exponent":2},"money":null},
           "remaining_amount_minor_units":5780,"scope":{"kind":"scoped","applies_to":[]},"shown_expires_at":null}],
         "next_expires_at":null}
        """;

    private static readonly string OrganizationsJson = $$"""
        [{"uuid":"{{ChatOrg}}","capabilities":["claude_max","chat"],"billing_type":"stripe_subscription"},
         {"uuid":"{{ApiOrg}}","capabilities":["api","api_individual","customer_terms:standard"],"billing_type":"prepaid"}]
        """;

    private const string SessionInvalidBody =
        """{"type":"error","error":{"type":"permission_error","message":"Invalid authorization","details":{"error_visibility":"user_facing","error_code":"account_session_invalid"}}}""";

    private const string PermissionDeniedBody =
        """{"type":"error","error":{"type":"permission_error","message":"Permission denied."},"code":"permission_denied"}""";

    private static void InLanguage(string lang, Action check)
    {
        var original = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage(lang);
            check();
        }
        finally
        {
            Loc.SetLanguage(original);
        }
    }

    [Fact]
    public void PromoTranchesBecomeRemainingGrantedAndExpiry()
    {
        Assert.True(ClaudeConsoleCreditService.TryParseCredits(CreditsJson, Now, out var credits));

        Assert.NotNull(credits);
        Assert.Equal(5780, credits.RemainingMinor);
        Assert.Equal(10000, credits.GrantedMinor);
        Assert.Equal(2, credits.Exponent);
        Assert.Equal("USD", credits.Currency);
        Assert.Equal(new DateTimeOffset(2026, 12, 10, 0, 0, 0, TimeSpan.Zero), credits.ExpiresAt);
        Assert.Equal(0.422, credits.UsedFraction, 3);
    }

    [Fact]
    public void ExpiredAndOtherCurrencyTranchesAreLeftOut()
    {
        const string json = """
            {"promo_tranches":[
              {"currency":"USD","expires_at":"2026-11-01T00:00:00Z","remaining_amount_minor_units":3000,"granted_amount_minor_units":10000},
              {"currency":"USD","expires_at":"2027-01-10T00:00:00Z","remaining_amount_minor_units":10000,"granted_amount_minor_units":10000},
              {"currency":"usd","expires_at":"2026-12-10T00:00:00Z","remaining_amount_minor_units":0,"granted_amount_minor_units":5000},
              {"currency":"EUR","expires_at":"2026-12-01T00:00:00Z","remaining_amount_minor_units":500,"granted_amount_minor_units":500}]}
            """;

        Assert.True(ClaudeConsoleCreditService.TryParseCredits(json, Now, out var credits));

        Assert.NotNull(credits);
        Assert.Equal(10000, credits.RemainingMinor);
        Assert.Equal(15000, credits.GrantedMinor);
        // 다 쓴 항목의 만료일은 의미가 없다 — 잔액이 남은 항목 중 가장 이른 날.
        Assert.Equal(new DateTimeOffset(2027, 1, 10, 0, 0, 0, TimeSpan.Zero), credits.ExpiresAt);
    }

    [Theory]
    [InlineData("""{"promo_tranches":[]}""")]
    [InlineData("""{"promo_tranches":null}""")]
    public void NoPromotionalCreditsIsASuccessfulReadWithNothingToShow(string json)
    {
        Assert.True(ClaudeConsoleCreditService.TryParseCredits(json, Now, out var credits));
        Assert.Null(credits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"amount":0}""")]
    [InlineData("[]")]
    public void UnexpectedBodyIsNotARead(string json)
    {
        Assert.False(ClaudeConsoleCreditService.TryParseCredits(json, Now, out _));
    }

    [Fact]
    public void OnlyApiOrganizationsAreProbed()
    {
        Assert.Equal([ApiOrg], ClaudeConsoleCreditService.ParseApiOrganizationIds(OrganizationsJson));
        Assert.Empty(ClaudeConsoleCreditService.ParseApiOrganizationIds("""[{"uuid":"not-a-uuid","capabilities":["api"]}]"""));
        Assert.Empty(ClaudeConsoleCreditService.ParseApiOrganizationIds("{}"));
    }

    private static Func<string, Task<ConsoleResponse>> Server(Dictionary<string, ConsoleResponse> routes, List<string>? calls = null) =>
        path =>
        {
            calls?.Add(path);
            return Task.FromResult(routes.TryGetValue(path, out var response) ? response : new ConsoleResponse(404, ""));
        };

    [Fact]
    public async Task SavedOrganizationIsReadWithoutListingOrganizations()
    {
        var calls = new List<string>();
        var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(Server(new()
        {
            [ClaudeConsoleCreditService.CreditsPath(ApiOrg)] = new(200, CreditsJson),
        }, calls), ApiOrg, Now);

        Assert.Equal(ClaudeConsoleSessionState.Ok, outcome.State);
        Assert.Equal(ApiOrg, outcome.OrgId);
        Assert.Equal(5780, outcome.Credits!.RemainingMinor);
        Assert.Equal([ClaudeConsoleCreditService.CreditsPath(ApiOrg)], calls);
    }

    [Fact]
    public async Task FirstConnectionFindsTheApiOrganization()
    {
        var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(Server(new()
        {
            [ClaudeConsoleCreditService.OrganizationsPath] = new(200, OrganizationsJson),
            [ClaudeConsoleCreditService.CreditsPath(ChatOrg)] = new(403, PermissionDeniedBody),
            [ClaudeConsoleCreditService.CreditsPath(ApiOrg)] = new(200, CreditsJson),
        }), null, Now);

        Assert.Equal(ClaudeConsoleSessionState.Ok, outcome.State);
        Assert.Equal(ApiOrg, outcome.OrgId);
    }

    [Fact]
    public async Task OrganizationWithoutPromoCreditsDoesNotHideALaterOneThatHasThem()
    {
        // PR #355 리뷰: 빈 조직(저장돼 있어도)에서 멈추면 Max 크레딧이 있는 개인 조직을 묻지 않았다.
        const string teamOrg = "99999999-8888-7777-6666-555555555555";
        var orgs = $$"""
            [{"uuid":"{{teamOrg}}","capabilities":["api"]},{"uuid":"{{ApiOrg}}","capabilities":["api","api_individual"]}]
            """;
        var routes = new Dictionary<string, ConsoleResponse>
        {
            [ClaudeConsoleCreditService.OrganizationsPath] = new(200, orgs),
            [ClaudeConsoleCreditService.CreditsPath(teamOrg)] = new(200, """{"promo_tranches":[]}"""),
            [ClaudeConsoleCreditService.CreditsPath(ApiOrg)] = new(200, CreditsJson),
        };

        foreach (var saved in new[] { null, teamOrg })
        {
            var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(Server(routes), saved, Now);
            Assert.Equal(ApiOrg, outcome.OrgId);
            Assert.Equal(5780, outcome.Credits!.RemainingMinor);
        }
    }

    [Fact]
    public async Task EmptyOrganizationIsKeptWhenNoOrganizationHasPromoCredits()
    {
        var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(Server(new()
        {
            [ClaudeConsoleCreditService.OrganizationsPath] = new(200, OrganizationsJson),
            [ClaudeConsoleCreditService.CreditsPath(ChatOrg)] = new(403, PermissionDeniedBody),
            [ClaudeConsoleCreditService.CreditsPath(ApiOrg)] = new(200, """{"promo_tranches":[]}"""),
        }), null, Now);

        Assert.Equal(ClaudeConsoleSessionState.Ok, outcome.State);
        Assert.Equal(ApiOrg, outcome.OrgId);
        Assert.Null(outcome.Credits);
    }

    [Fact]
    public async Task ExpiredSessionIsSignedOut()
    {
        var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(Server(new()
        {
            [ClaudeConsoleCreditService.CreditsPath(ApiOrg)] = new(403, SessionInvalidBody),
            [ClaudeConsoleCreditService.OrganizationsPath] = new(403, SessionInvalidBody),
        }), ApiOrg, Now);

        Assert.Equal(ClaudeConsoleSessionState.SignedOut, outcome.State);
    }

    [Fact]
    public async Task ForbiddenWithoutTheSessionCodeIsNotTreatedAsSignedOut()
    {
        // 예: Cloudflare 차단 페이지 — 멀쩡한 세션에 "다시 연결" 을 들이밀지 않는다.
        var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(Server(new()
        {
            [ClaudeConsoleCreditService.OrganizationsPath] = new(403, "<html>Just a moment...</html>"),
        }), null, Now);

        Assert.Equal(ClaudeConsoleSessionState.Unavailable, outcome.State);
    }

    [Fact]
    public async Task NoResponseIsUnavailable()
    {
        var outcome = await ClaudeConsoleCreditService.ReadCreditsAsync(
            _ => Task.FromResult(ConsoleResponse.NoResponse), ApiOrg, Now);

        Assert.Equal(ClaudeConsoleSessionState.Unavailable, outcome.State);
    }

    [Fact]
    public void ConsoleOriginIsOnlyPlatformClaudeCom()
    {
        Assert.True(ClaudeConsoleCreditService.IsConsoleOrigin("https://platform.claude.com/settings/billing"));
        Assert.False(ClaudeConsoleCreditService.IsConsoleOrigin("https://claude.ai/settings"));
        Assert.False(ClaudeConsoleCreditService.IsConsoleOrigin("http://platform.claude.com/"));
        Assert.False(ClaudeConsoleCreditService.IsConsoleOrigin("https://platform.claude.com:8443/"));
        Assert.False(ClaudeConsoleCreditService.IsConsoleOrigin("https://accounts.google.com/"));
    }

    [Fact]
    public void FetchScriptEmbedsValuesAsJson()
    {
        var script = ClaudeConsoleCreditService.BuildFetchScript("id\"1", "https://platform.claude.com/api/organizations");

        Assert.Contains(JsonSerializer.Serialize("id\"1"), script);
        Assert.Contains("\"https://platform.claude.com/api/organizations\"", script);
        Assert.Contains("credentials: \"same-origin\"", script);
    }

    [Fact]
    public void SavedOrganizationSurvivesARestart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cut-console-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.False(new ClaudeConsoleCreditService(dir).HasSavedSession);
            ClaudeConsoleCreditService.WriteOrgId(Path.Combine(dir, "console-org.txt"), ApiOrg);
            Assert.True(new ClaudeConsoleCreditService(dir).HasSavedSession);

            File.WriteAllText(Path.Combine(dir, "console-org.txt"), "../../evil");
            Assert.False(new ClaudeConsoleCreditService(dir).HasSavedSession);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task NotConnectedPcNeverStartsABrowser()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cut-console-" + Guid.NewGuid().ToString("N"));
        using var service = new ClaudeConsoleCreditService(dir);

        var result = await service.TryGetCreditsAsync();

        Assert.Equal(ClaudeConsoleSessionState.NotConnected, result.State);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void RowTextShowsRemainingGrantedAndExpiry()
    {
        var credits = new ClaudeConsoleCredits(5780, 10000, 2, "USD", new DateTimeOffset(2026, 12, 10, 12, 0, 0, TimeSpan.Zero));
        var date = credits.ExpiresAt!.Value.ToLocalTime().ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        InLanguage("ko", () => Assert.Equal($"$57.80 남음 / $100.00 · {date} 만료", ClaudeExtraUsageText.DescribeConsoleCredits(credits)));
        InLanguage("en", () => Assert.Equal($"$57.80 left / $100.00 · expires {date}", ClaudeExtraUsageText.DescribeConsoleCredits(credits)));
        InLanguage("en", () => Assert.Equal("$57.80 left / $100.00",
            ClaudeExtraUsageText.DescribeConsoleCredits(credits with { ExpiresAt = null })));
    }

    [Fact]
    public void ViewModelShowsRowOnlyWithCreditsAndAsksToReconnectWhenSignedOut()
    {
        var vm = new ClaudeViewModel();
        var credits = new ClaudeConsoleCredits(5780, 10000, 2, "USD", null);

        vm.ApplyConsoleCredits(ClaudeConsoleReadResult.Ok(credits));
        Assert.True(vm.ConsoleCreditsVisible);
        Assert.Equal(0.422, vm.ConsoleCreditsPercent, 3);
        Assert.False(vm.ConsoleNeedsReconnect);

        vm.ApplyConsoleCredits(ClaudeConsoleReadResult.SignedOut);
        Assert.False(vm.ConsoleCreditsVisible);
        Assert.True(vm.ConsoleNeedsReconnect);

        vm.ApplyConsoleCredits(ClaudeConsoleReadResult.Ok(null));
        Assert.False(vm.ConsoleCreditsVisible);
        Assert.False(vm.ConsoleNeedsReconnect);

        vm.ApplyConsoleCredits(ClaudeConsoleReadResult.NotConnected);
        Assert.False(vm.ConsoleCreditsVisible);
        Assert.Equal("", vm.ConsoleCreditsLabel);
    }

    [Fact]
    public void OldSettingsFileKeepsCreditsVisible()
    {
        var settings = JsonSerializer.Deserialize<NotificationSettings>("{}")!;
        Assert.True(settings.ShowClaudeExtraCredits);
        Assert.True(settings.ShowConsoleCredits);
        Assert.True(settings.ShowCodexCredits);
        Assert.True(settings.ShowAntigravityCredits);
    }
}
