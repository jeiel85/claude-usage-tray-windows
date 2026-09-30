using System.IO;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 같은 ntfy 토픽을 쓰는 서로 다른 계정의 알림이 같은 사건으로 오인되지 않게, 알림 키에 섞는 Codex 계정 식별자(#175).
// 같은 계정이면 어느 PC 에서든 같은 값이어야 하므로 갱신 때마다 바뀌는 토큰은 절대 쓰지 않는다.
public class CodexAccountIdentityTests
{
    private static string TempAuthPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"codex-account-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "auth.json");
    }

    /// <summary>서명 없는 id_token 을 만든다(payload 만 base64url) — Codex CLI 가 읽는 클레임 이름 그대로.</summary>
    private static string IdToken(string authClaimsJson)
    {
        var payload = "{\"https://api.openai.com/auth\":" + authClaimsJson + "}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "header." + encoded + ".signature";
    }

    private static void Write(string path, string? accountId, string? idToken, string accessToken = "access-1")
    {
        var tokens = new Dictionary<string, object?> { ["access_token"] = accessToken, ["refresh_token"] = "refresh-1" };
        if (accountId is not null) tokens["account_id"] = accountId;
        if (idToken is not null) tokens["id_token"] = idToken;
        File.WriteAllText(path, JsonSerializer.Serialize(new { auth_mode = "chatgpt", tokens }));
    }

    [Fact]
    public void ReadsAccountIdFromTokens_AndUserIdFromIdTokenClaims()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", IdToken("""{"chatgpt_user_id":"user-9","chatgpt_plan_type":"plus"}"""));

        Assert.Equal("acct-1|user-9", CodexUsageMonitor.TryReadAccountIdentity(path));
    }

    // Codex CLI 소스는 chatgpt_user_id 가 없으면 user_id 로 폴백한다.
    [Fact]
    public void FallsBackToUserIdClaim()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", IdToken("""{"user_id":"user-legacy"}"""));

        Assert.Equal("acct-1|user-legacy", CodexUsageMonitor.TryReadAccountIdentity(path));
    }

    [Fact]
    public void FallsBackToAccountIdClaim_WhenTokensLackIt()
    {
        var path = TempAuthPath();
        Write(path, accountId: null, IdToken("""{"chatgpt_account_id":"acct-claim","chatgpt_user_id":"user-9"}"""));

        Assert.Equal("acct-claim|user-9", CodexUsageMonitor.TryReadAccountIdentity(path));
    }

    [Fact]
    public void AccountIdAlone_IsEnough()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", idToken: null);

        Assert.Equal("acct-1|", CodexUsageMonitor.TryReadAccountIdentity(path));
    }

    // 핵심: 토큰이 갱신되어도(=파일이 바뀌어도) 같은 계정이면 식별자는 그대로여야 한다.
    [Fact]
    public void TokenRefresh_DoesNotChangeTheIdentity()
    {
        var path = TempAuthPath();
        var idToken = IdToken("""{"chatgpt_user_id":"user-9"}""");

        Write(path, "acct-1", idToken, accessToken: "access-OLD");
        var before = CodexUsageMonitor.TryReadAccountIdentity(path);

        Write(path, "acct-1", idToken, accessToken: "access-NEW");
        var after = CodexUsageMonitor.TryReadAccountIdentity(path);

        Assert.NotNull(before);
        Assert.Equal(before, after);
    }

    [Fact]
    public void DifferentUsersInTheSameWorkspace_GetDifferentIdentities()
    {
        var alice = TempAuthPath();
        var bob = TempAuthPath();
        Write(alice, "workspace-1", IdToken("""{"chatgpt_user_id":"alice"}"""));
        Write(bob, "workspace-1", IdToken("""{"chatgpt_user_id":"bob"}"""));

        Assert.NotEqual(CodexUsageMonitor.TryReadAccountIdentity(alice), CodexUsageMonitor.TryReadAccountIdentity(bob));
    }

    // 로그아웃·손상·형식 불일치는 "계정 모름"(null) — 예외로 알림 경로를 끊으면 안 된다.
    [Fact]
    public void ReturnsNull_WhenFileIsMissing()
        => Assert.Null(CodexUsageMonitor.TryReadAccountIdentity(
            Path.Combine(Path.GetTempPath(), $"codex-missing-{Guid.NewGuid():N}", "auth.json")));

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"tokens":"oops"}""")]
    [InlineData("""{"tokens":{}}""")]
    [InlineData("""{"tokens":{"account_id":"   "}}""")]
    [InlineData("""{"tokens":{"id_token":"not-a-jwt"}}""")]
    public void ReturnsNull_ForLoggedOutOrMalformedFiles(string content)
    {
        var path = TempAuthPath();
        File.WriteAllText(path, content);

        Assert.Null(CodexUsageMonitor.TryReadAccountIdentity(path));
    }

    // 쓰는 중인 파일(Codex CLI 가 토큰을 갱신하는 순간)도 읽혀야 한다 — FileShare.ReadWrite.
    [Fact]
    public void CanReadWhileAnotherProcessHoldsTheFileOpenForWriting()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", IdToken("""{"chatgpt_user_id":"user-9"}"""));

        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("acct-1|user-9", CodexUsageMonitor.TryReadAccountIdentity(path));
    }
}
