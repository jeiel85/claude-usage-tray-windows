using System;
using System.IO;
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

    private void Write(string json) => File.WriteAllText(_path, json);
}
