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
}