using System.IO;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

// 테스트가 실제 사용자 설정(~/.claude/claude-usage-tray.json)을 건드리지 않도록 경로를 주입받는다(#186).
public class SettingsServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cut-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "claude-usage-tray.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SaveThenLoad_UsesTheInjectedPath()
    {
        new SettingsService(_path).Save(new NotificationSettings { NtfyTopic = "synthetic-topic-for-tests-0001" });

        Assert.True(File.Exists(_path));
        Assert.Equal("synthetic-topic-for-tests-0001", new SettingsService(_path).Load().NtfyTopic);
    }

    [Fact]
    public void Load_WithoutAFile_ReturnsDefaultsAndCreatesNothing()
    {
        var settings = new SettingsService(_path).Load();

        Assert.Equal(new NotificationSettings().TrayDisplayMode, settings.TrayDisplayMode);
        Assert.False(File.Exists(_path));
    }

    // 계정 별명(#307)은 계정 키마다 저장되고, 키가 없던 예전 파일은 빈 사전으로 읽힌다.
    [Fact]
    public void ClaudeAccountAliases_RoundTripAndDefaultToEmpty()
    {
        var changedAt = new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);
        new SettingsService(_path).Save(new NotificationSettings
        {
            ClaudeAccountAliases = new() { ["org-1|acct"] = "개인", ["org-2|acct"] = "회사" },
            // 바꾼 시각(#313) — 지운 별명(org-3)도 시각만 남는다.
            ClaudeAccountAliasUpdatedAt = new() { ["org-2|acct"] = changedAt, ["org-3|acct"] = changedAt },
        });
        var loaded = new SettingsService(_path).Load();
        Assert.Equal("개인", loaded.ClaudeAccountAliases["org-1|acct"]);
        Assert.Equal("회사", loaded.ClaudeAccountAliases["org-2|acct"]);
        Assert.Equal(changedAt, loaded.ClaudeAccountAliasUpdatedAt["org-3|acct"]);

        File.WriteAllText(_path, """{ "PcAlias": "집" }""");
        Assert.Empty(new SettingsService(_path).Load().ClaudeAccountAliases);
        Assert.Empty(new SettingsService(_path).Load().ClaudeAccountAliasUpdatedAt);
    }

    // 이관은 읽을 때마다 메모리에서만 적용되고, 파일은 다음 저장 때 새 형식으로 바뀐다.
    [Fact]
    public void Load_MigratesALegacyFileWithoutRewritingIt()
    {
        const string legacy = """{ "TrayDisplayMode": "gemini-cli", "VisibleProviders": ["claude", "gemini-cli"] }""";
        File.WriteAllText(_path, legacy);

        var settings = new SettingsService(_path).Load();

        Assert.Equal(UsageProviderKind.Antigravity, settings.TrayDisplayMode);
        Assert.Equal(legacy, File.ReadAllText(_path));
    }
}
