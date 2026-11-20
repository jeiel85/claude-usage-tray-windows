using System.IO;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class HistoryServiceTests
{
    [Fact]
    public void CanInstantiate()
    {
        var service = new ClaudeUsageTray.Services.HistoryService();
        Assert.NotNull(service);
    }

    // 테스트가 실제 history(~/.claude/claude-usage-tray-history-*.json)를 건드리지 않도록 폴더를 주입받는다(#186).
    [Fact]
    public void RecordToday_WritesOnlyIntoTheInjectedDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cut-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            new ClaudeUsageTray.Services.HistoryService(dir)
                .RecordToday(ClaudeUsageTray.Models.UsageProviderKind.Codex, null, 10, 20, 0, 0, 1);

            Assert.True(File.Exists(Path.Combine(dir, "claude-usage-tray-history-codex.json")));
            var reloaded = Assert.Single(new ClaudeUsageTray.Services.HistoryService(dir)
                .GetLast(ClaudeUsageTray.Models.UsageProviderKind.Codex, null, 1));
            Assert.Equal(10, reloaded.InputTokens);
            Assert.Equal(20, reloaded.OutputTokens);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    // #275: 계정을 모르던 동안 계정 없는 scope 에 쌓인 기록은, 이 PC 가 처음 알게 된 계정이 이어 받는다.
    [Fact]
    public void FirstKnownAccount_InheritsHistoryRecordedWhileAccountWasUnknown()
    {
        var dir = NewDirectory();
        try
        {
            new ClaudeUsageTray.Services.HistoryService(dir)
                .RecordToday(ClaudeUsageTray.Models.UsageProviderKind.Claude, null, 10, 20, 0, 0, 1);

            var service = new ClaudeUsageTray.Services.HistoryService(dir);
            var inherited = Assert.Single(service.GetLast(ClaudeUsageTray.Models.UsageProviderKind.Claude, "org-a", 7));
            Assert.Equal(10, inherited.InputTokens);
            // 자동 목표(최근 최대치)도 이어진다.
            Assert.Equal(30, service.GetRecentMaxTotalTokens(ClaudeUsageTray.Models.UsageProviderKind.Claude, "org-a", 7));

            // 이어 받은 기록은 계정 파일로 저장되고, 원본(계정 없는 scope)은 지우지 않는다.
            Assert.True(File.Exists(Path.Combine(dir, "claude-usage-tray-history-claude-org-a.json")));
            Assert.True(File.Exists(Path.Combine(dir, "claude-usage-tray-history-claude.json")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void LaterAccount_DoesNotInheritAnotherAccountsHistory()
    {
        var dir = NewDirectory();
        try
        {
            var first = new ClaudeUsageTray.Services.HistoryService(dir);
            first.RecordToday(ClaudeUsageTray.Models.UsageProviderKind.Claude, null, 10, 20, 0, 0, 1);
            Assert.Single(first.GetLast(ClaudeUsageTray.Models.UsageProviderKind.Claude, "org-a", 7));

            // 이미 계정 scope 가 있는 PC 에서 다른 계정으로 바꾸면 빈 기록에서 시작한다(남의 기록을 내 것처럼 보이지 않게).
            var later = new ClaudeUsageTray.Services.HistoryService(dir);
            Assert.Empty(later.GetLast(ClaudeUsageTray.Models.UsageProviderKind.Claude, "org-b", 7));
            Assert.False(File.Exists(Path.Combine(dir, "claude-usage-tray-history-claude-org-b.json")));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ExistingAccountHistory_IsNotOverwrittenByUnscopedHistory()
    {
        var dir = NewDirectory();
        try
        {
            var service = new ClaudeUsageTray.Services.HistoryService(dir);
            service.RecordToday(ClaudeUsageTray.Models.UsageProviderKind.Claude, "org-a", 1, 2, 0, 0, 1);
            service.RecordToday(ClaudeUsageTray.Models.UsageProviderKind.Claude, null, 10, 20, 0, 0, 1);

            var reloaded = Assert.Single(new ClaudeUsageTray.Services.HistoryService(dir)
                .GetLast(ClaudeUsageTray.Models.UsageProviderKind.Claude, "org-a", 7));
            Assert.Equal(1, reloaded.InputTokens);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static string NewDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cut-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
