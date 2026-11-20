using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;

namespace ClaudeUsageTray.ViewModels;

/// <summary>
/// "오늘 N개 세션" 목록의 한 줄. 값은 만들 때 확정되고 이후 바뀌지 않는다(새로고침마다 새로 만든다).
///
/// 표시 문자열은 일부러 언어 중립(경로·브랜치·시각·토큰 수)으로만 채운다 — 언어를 바꿔도
/// 다음 조회를 기다리지 않고 화면이 맞아떨어지게 하기 위해서다. 번역이 필요한 문구는
/// 목록 바깥(머리말·안내)에 둔다.
/// </summary>
public class SessionListItem
{
    /// <summary>최근 활동으로 볼 시간 창. 이 안에 마지막 기록이 있으면 "돌고 있는 세션"으로 표시한다.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(10);

    public string DisplayName { get; }
    public string ContextLabel { get; }
    public string TimeLabel { get; }
    public string TokenLabel { get; }
    public string Tooltip { get; }
    public bool IsRecent { get; }

    public SessionListItem(SessionInfo session, DateTime nowUtc)
    {
        var local = session.LastActivityUtc == default
            ? (DateTime?)null
            : session.LastActivityUtc.ToLocalTime();

        DisplayName  = BuildDisplayName(session);
        ContextLabel = BuildContextLabel(session);
        TimeLabel    = local?.ToString("HH:mm") ?? "";
        TokenLabel   = UsageCalculator.FormatTokenShort(session.TotalTokens);
        IsRecent     = local.HasValue && nowUtc - session.LastActivityUtc <= RecentWindow;
        Tooltip      = BuildTooltip(session, local);
    }

    /// <summary>
    /// 목록에 쓸 이름. 워크트리(<c>&lt;repo&gt;\.claude\worktrees\&lt;name&gt;</c>)면 마지막 폴더가
    /// 해시 섞인 워크트리 이름이라 알아볼 수 없으므로, 그 위의 저장소 이름을 쓴다.
    /// </summary>
    private static string BuildDisplayName(SessionInfo session)
    {
        var segments = SplitPath(session.ProjectPath);
        if (segments.Length == 0)
            return string.IsNullOrEmpty(session.SessionId) ? "?" : ShortId(session.SessionId);

        var worktreeRoot = IndexOfWorktreeRoot(segments);
        return worktreeRoot > 0 ? segments[worktreeRoot - 1] : segments[^1];
    }

    /// <summary>이름 아래 한 줄 — 브랜치가 있으면 브랜치, 없으면 워크트리 이름, 그것도 없으면 세션 id.</summary>
    private static string BuildContextLabel(SessionInfo session)
    {
        if (!string.IsNullOrEmpty(session.GitBranch)) return session.GitBranch;

        var segments = SplitPath(session.ProjectPath);
        if (segments.Length > 0 && IndexOfWorktreeRoot(segments) > 0) return segments[^1];

        return string.IsNullOrEmpty(session.SessionId) ? "" : ShortId(session.SessionId);
    }

    private static string BuildTooltip(SessionInfo session, DateTime? localActivity)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(session.Title)) lines.Add(session.Title);
        if (!string.IsNullOrEmpty(session.ProjectPath)) lines.Add(session.ProjectPath);
        if (!string.IsNullOrEmpty(session.GitBranch)) lines.Add(session.GitBranch);

        var stamp = localActivity?.ToString("yyyy-MM-dd HH:mm") ?? "";
        lines.Add($"{stamp}  ·  {session.TotalTokens:N0} tok".Trim());

        return string.Join('\n', lines);
    }

    private static string[] SplitPath(string path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>경로에서 <c>.claude\worktrees</c> 가 시작되는 위치. 없으면 -1.</summary>
    private static int IndexOfWorktreeRoot(string[] segments)
    {
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (string.Equals(segments[i], ".claude", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[i + 1], "worktrees", StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string ShortId(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];
}
