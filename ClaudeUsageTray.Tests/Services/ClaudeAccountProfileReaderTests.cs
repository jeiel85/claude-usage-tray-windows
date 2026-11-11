using System.IO;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public sealed class ClaudeAccountProfileReaderTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public ClaudeAccountProfileReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cut-account-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static string Profile(string displayName, string organizationName, string organizationType) => $$"""
        {
          "numStartups": 12,
          "projects": { "D:/work": { "allowedTools": [] } },
          "oauthAccount": {
            "accountUuid": "00000000-0000-0000-0000-000000000001",
            "emailAddress": "someone@example.com",
            "organizationUuid": "00000000-0000-0000-0000-000000000002",
            "displayName": "{{displayName}}",
            "fullName": "{{displayName}}",
            "organizationRole": "user",
            "organizationName": "{{organizationName}}",
            "organizationType": "{{organizationType}}"
          }
        }
        """;

    [Fact]
    public void TeamAccount_ShowsNameAndOrganization()
    {
        File.WriteAllText(_path, Profile("김민준", "Acme Corp", "claude_team"));
        Assert.Equal("김민준 · Acme Corp", new ClaudeAccountProfileReader(_path).GetLabel());
    }

    [Fact]
    public void PersonalAccount_HidesAutoNamedOrganization()
    {
        // 개인 구독의 조직 이름은 이메일로 자동 생성될 수 있다 — 조직 종류가 팀·엔터프라이즈가 아니면 붙이지 않는다.
        File.WriteAllText(_path, Profile("홍길동", "someone@example.com's Organization", "claude_max"));
        Assert.Equal("홍길동", new ClaudeAccountProfileReader(_path).GetLabel());
    }

    [Fact]
    public void EmailNeverReachesTheLabel()
    {
        // 이름 자리에 이메일이 들어 있거나, 팀 조직 이름에 이메일이 섞여 있어도 쓰지 않는다.
        File.WriteAllText(_path, Profile("someone@example.com", "someone@example.com's Organization", "claude_team"));
        Assert.Equal("", new ClaudeAccountProfileReader(_path).GetLabel());
    }

    [Fact]
    public void MissingDisplayName_FallsBackToFullName()
    {
        File.WriteAllText(_path, """
            { "oauthAccount": { "fullName": "Kim Lee", "organizationName": "Acme", "organizationType": "claude_enterprise" } }
            """);
        Assert.Equal("Kim Lee · Acme", new ClaudeAccountProfileReader(_path).GetLabel());
    }

    [Theory]
    // 파일은 있지만 로그인 프로필이 없다(로그아웃 등).
    [InlineData("""{ "numStartups": 3 }""")]
    // 형식이 예상과 다르면 빈 문구로 끝낸다.
    [InlineData("""{ "oauthAccount": "not-an-object" }""")]
    [InlineData("""{ "oauthAccount": { "displayName": 42, "organizationName": null } }""")]
    [InlineData("""[]""")]
    public void NoUsableProfile_ReturnsEmpty(string json)
    {
        File.WriteAllText(_path, json);
        Assert.Equal("", new ClaudeAccountProfileReader(_path).GetLabel());
    }

    [Fact]
    public void MissingFile_ReturnsEmpty()
        => Assert.Equal("", new ClaudeAccountProfileReader(_path).GetLabel());

    [Fact]
    public void AccountSwitch_IsPickedUpOnNextRead()
    {
        var reader = new ClaudeAccountProfileReader(_path);
        File.WriteAllText(_path, Profile("김민준", "Acme Corp", "claude_team"));
        Assert.Equal("김민준 · Acme Corp", reader.GetLabel());

        File.WriteAllText(_path, Profile("홍길동", "personal", "claude_pro"));
        Assert.Equal("홍길동", reader.GetLabel());

        File.Delete(_path);
        Assert.Equal("", reader.GetLabel());
    }

    [Fact]
    public void HalfWrittenFile_KeepsPreviousLabelUntilReadable()
    {
        // Claude Code 가 파일을 다시 쓰는 도중에 읽으면 JSON 이 잘려 있다 — 그때는 이름이 깜빡이지 않게 직전 문구를 유지한다.
        var reader = new ClaudeAccountProfileReader(_path);
        File.WriteAllText(_path, Profile("김민준", "Acme Corp", "claude_team"));
        Assert.Equal("김민준 · Acme Corp", reader.GetLabel());

        File.WriteAllText(_path, """{ "oauthAccount": { "displayName": "홍""");
        Assert.Equal("김민준 · Acme Corp", reader.GetLabel());

        File.WriteAllText(_path, Profile("홍길동", "Acme", "claude_team"));
        Assert.Equal("홍길동 · Acme", reader.GetLabel());
    }

    [Fact]
    public void FileLockedForWriting_IsStillReadable()
    {
        // Claude Code 가 파일을 연 채로 있어도 읽는다(FileShare.ReadWrite).
        File.WriteAllText(_path, Profile("김민준", "Acme Corp", "claude_team"));
        using var writer = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        Assert.Equal("김민준 · Acme Corp", new ClaudeAccountProfileReader(_path).GetLabel());
    }

    [Theory]
    [InlineData("김민준", "Acme Corp", "claude_team", "김민준 · Acme Corp")]
    [InlineData("  김민준  ", "  Acme Corp ", "claude_team", "김민준 · Acme Corp")]
    [InlineData("김민준", "Acme Corp", "CLAUDE_ENTERPRISE", "김민준 · Acme Corp")]
    // 개인·모르는 조직 종류는 조직 이름을 쓰지 않는다.
    [InlineData("김민준", "Acme Corp", "claude_max", "김민준")]
    [InlineData("김민준", "Acme Corp", null, "김민준")]
    // 이름이 없으면 조직만.
    [InlineData(null, "Acme Corp", "claude_team", "Acme Corp")]
    [InlineData("", "", "claude_team", "")]
    [InlineData(null, null, null, "")]
    public void FormatLabel_JoinsWhatIsSafeToShow(string? name, string? org, string? orgType, string expected)
        => Assert.Equal(expected, ClaudeAccountProfileReader.FormatLabel(name, org, orgType));
}
