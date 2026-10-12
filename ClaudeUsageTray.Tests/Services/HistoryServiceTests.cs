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
}