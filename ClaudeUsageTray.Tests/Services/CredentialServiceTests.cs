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

    private void Write(string json) => File.WriteAllText(_path, json);
}
