using System;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class SessionMonitorTests
{
    [Fact]
    public void CanInstantiate()
    {
        // Verify the SessionMonitor can be instantiated
        var monitor = new ClaudeUsageTray.Services.SessionMonitor();
        Assert.NotNull(monitor);
    }
}