using System;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.ViewModels;

public class SessionListItemTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DisplayName_UsesLastFolder_ForPlainProject()
    {
        var item = Build(new SessionInfo
        {
            ProjectPath = @"D:\Project\claude-usage-tray-windows",
            GitBranch = "master"
        });

        Assert.Equal("claude-usage-tray-windows", item.DisplayName);
        Assert.Equal("master", item.ContextLabel);
    }

    /// <summary>
    /// 워크트리 경로의 마지막 폴더는 해시가 붙은 이름이라 목록에서 알아볼 수 없다.
    /// 저장소 이름을 보여주고, 어느 워크트리인지는 브랜치로 구분한다.
    /// </summary>
    [Fact]
    public void DisplayName_UsesRepoName_ForWorktreePath()
    {
        var item = Build(new SessionInfo
        {
            ProjectPath = @"D:\Project\my-repo\.claude\worktrees\some-task-3db433",
            GitBranch = "claude/some-task"
        });

        Assert.Equal("my-repo", item.DisplayName);
        Assert.Equal("claude/some-task", item.ContextLabel);
    }

    [Fact]
    public void ContextLabel_FallsBackToWorktreeFolder_WhenBranchUnknown()
    {
        var item = Build(new SessionInfo
        {
            ProjectPath = @"D:\Project\my-repo\.claude\worktrees\some-task-3db433"
        });

        Assert.Equal("some-task-3db433", item.ContextLabel);
    }

    [Fact]
    public void ContextLabel_FallsBackToShortSessionId_WhenPathUnknown()
    {
        var item = Build(new SessionInfo { SessionId = "06828cf3-231e-48d9-8360-045684782a13" });

        Assert.Equal("06828cf3", item.DisplayName);
        Assert.Equal("06828cf3", item.ContextLabel);
    }

    [Fact]
    public void IsRecent_IsTrue_OnlyWithinRecentWindow()
    {
        var recent = Build(new SessionInfo { LastActivityUtc = NowUtc.AddMinutes(-5) });
        var idle = Build(new SessionInfo { LastActivityUtc = NowUtc.AddMinutes(-30) });

        Assert.True(recent.IsRecent);
        Assert.False(idle.IsRecent);
    }

    [Fact]
    public void TimeLabel_IsEmpty_WhenActivityUnknown()
    {
        var item = Build(new SessionInfo { ProjectPath = @"D:\Project\repo" });

        Assert.Equal("", item.TimeLabel);
        Assert.False(item.IsRecent);
    }

    [Fact]
    public void Tooltip_CarriesTitleAndFullPath()
    {
        var item = Build(new SessionInfo
        {
            Title = "오늘 세션 목록 보여줘",
            ProjectPath = @"D:\Project\my-repo",
            GitBranch = "master",
            LastActivityUtc = NowUtc.AddMinutes(-1),
            TotalTokens = 1_234_567
        });

        Assert.Contains("오늘 세션 목록 보여줘", item.Tooltip);
        Assert.Contains(@"D:\Project\my-repo", item.Tooltip);
        Assert.Contains("master", item.Tooltip);
        Assert.Contains("1,234,567", item.Tooltip);
        Assert.Equal("1.2M", item.TokenLabel);
    }

    private static SessionListItem Build(SessionInfo session) => new(session, NowUtc);
}
