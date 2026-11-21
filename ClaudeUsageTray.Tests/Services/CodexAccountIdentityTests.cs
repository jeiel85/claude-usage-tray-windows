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

    /// <summary>최상위 클레임까지 넣은 서명 없는 id_token(이름·이메일은 OpenAI id_token 의 표준 클레임 자리).</summary>
    private static string IdTokenWithProfile(string? name, string? email = null)
    {
        var claims = new Dictionary<string, object?> { ["https://api.openai.com/auth"] = new { chatgpt_user_id = "user-9" } };
        if (name is not null) claims["name"] = name;
        if (email is not null) claims["email"] = email;
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(claims))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "header." + encoded + ".signature";
    }

    // 휴대폰 계정 카드·알림의 Codex 표시 이름(#266): 이름은 보내고 이메일은 보내지 않는다.
    [Fact]
    public void AccountName_IsTheNameClaim_NeverTheEmail()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", IdTokenWithProfile("  김민준  ", "minjun@example.com"));
        Assert.Equal("김민준", CodexUsageMonitor.TryReadAccountName(path));

        // 이름이 없으면 이메일로 대신하지 않는다.
        Write(path, "acct-1", IdTokenWithProfile(null, "minjun@example.com"));
        Assert.Null(CodexUsageMonitor.TryReadAccountName(path));

        // 이름 자리에 이메일이 들어 있는 계정도 보내지 않는다.
        Write(path, "acct-1", IdTokenWithProfile("minjun@example.com"));
        Assert.Null(CodexUsageMonitor.TryReadAccountName(path));
    }

    [Fact]
    public void AccountName_IsCapped_AndMissingFilesGiveNull()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", IdTokenWithProfile(new string('가', 200)));
        var name = CodexUsageMonitor.TryReadAccountName(path);
        Assert.NotNull(name);
        Assert.True(name!.Length <= CodexUsageMonitor.MaxAccountNameLength + 1);
        Assert.EndsWith("…", name);

        Assert.Null(CodexUsageMonitor.TryReadAccountName(Path.Combine(Path.GetDirectoryName(path)!, "missing.json")));
        Write(path, "acct-1", idToken: null);
        Assert.Null(CodexUsageMonitor.TryReadAccountName(path));
    }
    // 갱신마다 요금제·식별자·이름을 각각 물어도 파일은 한 번만 읽는다(#268). 길이·수정 시각이 같으면 지난 결과를 쓰고,
    // 바뀌면 다시 읽는다. 시각을 과거로 고정해 "방금 쓴 파일은 캐시를 믿지 않는" 유예를 지난 상태를 만든다.
    [Fact]
    public void ReadAuthInfo_ReusesTheParsedFile_UntilItChanges()
    {
        var path = TempAuthPath();
        var stamp = DateTime.UtcNow.AddHours(-1);
        Write(path, "acct-1", IdToken("""{"chatgpt_user_id":"user-A","chatgpt_plan_type":"plus"}"""));
        File.SetLastWriteTimeUtc(path, stamp);

        var first = CodexUsageMonitor.ReadAuthInfo(path);
        Assert.Equal(new CodexUsageMonitor.CodexAuthInfo("plus", "acct-1|user-A", null), first);

        // 길이·시각이 그대로인 다른 내용 — 캐시가 쓰였다면 이전 값이 나온다(파일을 다시 열지 않았다는 증거).
        Write(path, "acct-1", IdToken("""{"chatgpt_user_id":"user-B","chatgpt_plan_type":"plus"}"""));
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Same(first, CodexUsageMonitor.ReadAuthInfo(path));

        // 수정 시각이 바뀌면(Codex 가 다시 씀) 새로 읽는다.
        File.SetLastWriteTimeUtc(path, stamp.AddMinutes(1));
        Assert.Equal("acct-1|user-B", CodexUsageMonitor.ReadAuthInfo(path)?.AccountIdentity);
    }

    [Fact]
    public void ReadAuthInfo_DoesNotTrustTheCache_ForAFileJustWritten()
    {
        var path = TempAuthPath();
        Write(path, "acct-1", IdToken("""{"chatgpt_user_id":"user-A"}"""));
        var stamp = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Equal("acct-1|user-A", CodexUsageMonitor.TryReadAccountIdentity(path));

        // 같은 길이·같은 시각으로 곧바로 다시 쓰여도(시각 해상도 안의 연속 쓰기) 방금 쓴 파일이면 다시 읽는다.
        Write(path, "acct-1", IdToken("""{"chatgpt_user_id":"user-B"}"""));
        File.SetLastWriteTimeUtc(path, stamp);
        Assert.Equal("acct-1|user-B", CodexUsageMonitor.TryReadAccountIdentity(path));
    }
}
