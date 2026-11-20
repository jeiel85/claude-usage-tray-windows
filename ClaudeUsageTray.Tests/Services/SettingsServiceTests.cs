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

    // #331: 깨진 파일을 기본값으로 읽으면 다음 저장이 모든 설정을 덮어썼다. 직전 정상본(.bak)에서 읽고, 깨진 파일은 옆에 남긴다.
    [Fact]
    public void CorruptFile_IsKeptAsideAndTheLastGoodCopyIsUsed()
    {
        var service = new SettingsService(_path);
        service.Save(new NotificationSettings { NtfyTopic = "first-synthetic-topic-0001" });
        service.Save(new NotificationSettings { NtfyTopic = "second-synthetic-topic-002" });
        Assert.Equal("first-synthetic-topic-0001", new SettingsService(service.BackupPath).Load().NtfyTopic);

        File.WriteAllText(_path, """{ "NtfyTopic": "second-synth""");   // 쓰다 만 파일

        var loaded = service.Load();
        Assert.Equal("first-synthetic-topic-0001", loaded.NtfyTopic);
        var corrupt = Assert.Single(Directory.GetFiles(_dir, "claude-usage-tray.json.corrupt-*"));
        Assert.Contains("second-synth", File.ReadAllText(corrupt));

        // 다음 저장이 정상본을 기준으로 쓴다(기본값으로 덮어쓰지 않는다).
        service.Update(s => { s.PcAlias = "HOME"; return s; });
        var after = service.Load();
        Assert.Equal("first-synthetic-topic-0001", after.NtfyTopic);
        Assert.Equal("HOME", after.PcAlias);
    }

    [Fact]
    public void CorruptFileWithoutABackup_FallsBackToDefaultsButKeepsTheFile()
    {
        File.WriteAllText(_path, "not json at all");

        var loaded = new SettingsService(_path).Load();

        Assert.Equal(new NotificationSettings().NtfyTopic, loaded.NtfyTopic);
        Assert.False(File.Exists(_path));
        Assert.Equal("not json at all", File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "*.corrupt-*"))));
    }

    [Fact]
    public void Save_ReplacesTheFileWholeAndLeavesNoTemporaryFile()
    {
        var service = new SettingsService(_path);
        service.Save(new NotificationSettings { NtfyTopic = "first-synthetic-topic-0001" });
        Assert.False(File.Exists(service.BackupPath)); // 처음 쓰기에는 바꿀 파일이 없다
        service.Save(new NotificationSettings { NtfyTopic = "second-synthetic-topic-002" });

        Assert.Equal(["claude-usage-tray.json", "claude-usage-tray.json.bak"],
            Directory.GetFiles(_dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal("second-synthetic-topic-002", service.Load().NtfyTopic);
    }

    [Fact]
    public void OnlyTheBackupLeft_IsRead()
    {
        // 바꿔 끼우는 순간에 꺼지면 .bak 만 남을 수 있다.
        var service = new SettingsService(_path);
        service.Save(new NotificationSettings { NtfyTopic = "first-synthetic-topic-0001" });
        service.Save(new NotificationSettings { NtfyTopic = "second-synthetic-topic-002" });
        File.Delete(_path);

        Assert.Equal("first-synthetic-topic-0001", service.Load().NtfyTopic);
    }

    // #331: 읽기·바꾸기·쓰기가 겹치면 한쪽 변경이 사라졌다(UI 저장과 자동 업데이트 표식 기록 등).
    [Fact]
    public void ConcurrentUpdates_KeepEveryChange()
    {
        var services = Enumerable.Range(0, 4).Select(_ => new SettingsService(_path)).ToArray();
        Parallel.For(0, 64, i =>
        {
            services[i % services.Length].Update(s =>
            {
                s.ClaudeAccountAliases[$"key-{i}"] = $"alias-{i}";
                return s;
            });
            Assert.NotNull(services[(i + 1) % services.Length].Load());
        });

        var aliases = new SettingsService(_path).Load().ClaudeAccountAliases;
        Assert.Equal(64, aliases.Count);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
    }

    [Fact]
    public void Update_ReturningNull_WritesNothing()
    {
        var service = new SettingsService(_path);
        Assert.Null(service.Update(_ => null));
        Assert.False(File.Exists(_path));
    }

    // #334 리뷰: NaN·∞ 는 JSON 으로 쓸 수 없어 ArgumentException 이 난다. 저장 실패가 앱을 끝내지 않고, 원래 파일도 그대로다.
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void UnserializableValue_FailsTheSaveWithoutThrowingOrTouchingTheFile(double bad)
    {
        var service = new SettingsService(_path);
        service.Save(new NotificationSettings { NtfyTopic = "first-synthetic-topic-0001" });
        var before = File.ReadAllText(_path);

        service.Save(new NotificationSettings { NtfyTopic = "second-synthetic-topic-002", WeatherHighTemperatureThresholdC = bad });
        service.Update(s => { s.WeatherHighTemperatureThresholdC = bad; return s; });

        Assert.Equal(before, File.ReadAllText(_path));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    // #334 리뷰: 잠시 못 읽는 멀쩡한 파일을 깨진 것으로 보고 옮기면, 문제가 풀린 뒤에도 옛 .bak 이나 기본값으로 읽었다.
    [Fact]
    public void UnreadableFile_IsLeftInPlace()
    {
        var service = new SettingsService(_path);
        service.Save(new NotificationSettings { NtfyTopic = "first-synthetic-topic-0001" });
        service.Save(new NotificationSettings { NtfyTopic = "second-synthetic-topic-002" });

        // 다른 프로그램이 읽기를 막고 지우기만 허락한 채 열고 있다.
        using (new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
        {
            Assert.Equal("first-synthetic-topic-0001", service.Load().NtfyTopic); // 이번에는 직전 정상본
            Assert.True(File.Exists(_path));
            Assert.Empty(Directory.GetFiles(_dir, "*.corrupt-*"));
        }

        Assert.Equal("second-synthetic-topic-002", service.Load().NtfyTopic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{ \"NtfyTopic\": ")]
    public void OnlyContentThatFailsToParse_CountsAsCorrupt(string content)
    {
        File.WriteAllText(_path, content);
        Assert.Equal(SettingsService.ReadOutcome.Corrupt, SettingsService.TryRead(_path).Item1);
        Assert.Equal(SettingsService.ReadOutcome.Missing, SettingsService.TryRead(_path + ".none").Item1);
    }
}
