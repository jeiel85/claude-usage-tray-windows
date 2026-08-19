using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// ~/.claude/projects 아래 세션 트랜스크립트(*.jsonl)에서 "오늘" 사용량을 집계한다.
///
/// 집계는 항상 <b>오늘 총량</b>이어야 한다 — 호출자(HistoryService.RecordToday)가 결과를
/// 그날 항목에 덮어쓰기 때문에, 부분 합계를 돌려주면 그대로 히스토리가 오염된다.
/// 그래서 증분(마지막 읽은 오프셋 이후만 읽기) 방식을 쓰지 않고, 오늘 기록됐을 수 있는
/// 파일만 골라 매번 전체를 다시 읽는다. 파일 목록 필터가 mtime 기반이라 실제로 여는 파일은
/// 보통 몇 개뿐이다.
/// </summary>
public class SessionMonitor
{
    private static readonly string DefaultProjectsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    /// <summary>
    /// mtime 필터의 여유분. 파일시스템 타임스탬프 정밀도·시계 오차로 자정 직후 기록이
    /// 전날 mtime 을 갖는 경우를 대비한다. 몇 개 더 여는 비용이 누락보다 싸다.
    /// </summary>
    private static readonly TimeSpan MTimeTolerance = TimeSpan.FromHours(1);

    /// <summary>세션 제목(첫 프롬프트)을 잘라 보관할 최대 길이 — 팝업 툴팁 한 줄 분량.</summary>
    private const int MaxTitleLength = 80;

    private readonly string _projectsPath;

    /// <param name="projectsPath">트랜스크립트 루트. null 이면 ~/.claude/projects (테스트용 주입점).</param>
    public SessionMonitor(string? projectsPath = null)
    {
        _projectsPath = projectsPath ?? DefaultProjectsPath;
    }

    public SessionStats ScanTodayUsage()
    {
        var stats = new SessionStats();
        var today = DateTime.Today;

        if (!Directory.Exists(_projectsPath)) return stats;

        foreach (var file in EnumerateFilesTouchedSince(today))
        {
            try
            {
                ProcessFile(file, today, stats);
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return stats;
    }

    /// <summary>
    /// 오늘 기록이 들어 있을 수 있는 파일만 추린다.
    /// 마지막 쓰기가 오늘 이전이면 오늘자 항목이 있을 수 없으므로 열지 않는다.
    /// </summary>
    private IEnumerable<string> EnumerateFilesTouchedSince(DateTime sinceLocalDate)
    {
        var cutoffUtc = sinceLocalDate.ToUniversalTime() - MTimeTolerance;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(_projectsPath, "*.jsonl", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var file in files)
        {
            bool touched;
            try
            {
                touched = File.GetLastWriteTimeUtc(file) >= cutoffUtc;
            }
            catch
            {
                // 상태를 못 읽으면 누락보다 한 번 더 읽는 쪽을 택한다
                touched = true;
            }

            if (touched) yield return file;
        }
    }

    private static void ProcessFile(string filePath, DateTime sinceDate, SessionStats stats)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        string? line;
        bool fileHadActivity = false;
        // 트랜스크립트에 sessionId 가 없는 옛 형식이면 파일명이 곧 세션 id 다.
        var session = new SessionInfo { SessionId = Path.GetFileNameWithoutExtension(filePath) };

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // cwd·브랜치는 종류를 가리지 않고 모든 줄에 실린다. 나중 줄이 최신 상태다.
                CaptureSessionMeta(root, session);

                if (!root.TryGetProperty("type", out var typeEl)) continue;
                var entryType = typeEl.GetString();

                // 제목은 날짜와 무관하게 세션의 첫 사람 프롬프트에서 뽑는다
                // (어제 시작해 오늘까지 이어진 세션도 알아볼 수 있어야 한다).
                if (entryType == "user")
                {
                    if (session.Title.Length == 0) TryCaptureTitle(root, session);
                    continue;
                }

                // Only process assistant messages with usage data
                if (entryType != "assistant") continue;

                // Check timestamp
                DateTime parsedTs = default;
                if (root.TryGetProperty("timestamp", out var tsEl))
                {
                    if (DateTime.TryParse(tsEl.GetString(), out parsedTs))
                    {
                        if (parsedTs.ToLocalTime().Date < sinceDate) continue;
                        fileHadActivity = true;
                        var tsUtc = parsedTs.ToUniversalTime();
                        if (tsUtc > stats.LastActivity) stats.LastActivity = tsUtc;
                        if (tsUtc > session.LastActivityUtc) session.LastActivityUtc = tsUtc;
                    }
                }

                // Extract usage from message
                if (!root.TryGetProperty("message", out var msgEl)) continue;
                if (!msgEl.TryGetProperty("usage", out var usageEl)) continue;

                long inp_ = 0, out_ = 0, cr_ = 0, cw_ = 0;
                if (usageEl.TryGetProperty("input_tokens", out var inp)) { inp_ = inp.GetInt64(); stats.TotalInputTokens += inp_; }
                if (usageEl.TryGetProperty("output_tokens", out var outp)) { out_ = outp.GetInt64(); stats.TotalOutputTokens += out_; }
                if (usageEl.TryGetProperty("cache_read_input_tokens", out var cr)) { cr_ = cr.GetInt64(); stats.TotalCacheReadTokens += cr_; }
                if (usageEl.TryGetProperty("cache_creation_input_tokens", out var cw)) { cw_ = cw.GetInt64(); stats.TotalCacheWriteTokens += cw_; }

                session.TotalTokens += inp_ + out_ + cr_ + cw_;

                // 시간대별 집계 (로컬 시간 기준, parsedTs 재사용)
                if (parsedTs != default)
                    stats.HourlyTokens[parsedTs.ToLocalTime().Hour] += inp_ + out_ + cr_ + cw_;

                // Check for rate limit hit
                if (root.TryGetProperty("error", out var errEl) && errEl.GetString() == "rate_limit")
                {
                    stats.HasRateLimitHit = true;
                    // Try to extract reset time from message content
                    if (msgEl.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentEl.EnumerateArray())
                        {
                            if (item.TryGetProperty("text", out var textEl))
                            {
                                var text = textEl.GetString() ?? "";
                                if (text.Contains("resets"))
                                    stats.RateLimitResetTime = text;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Skip malformed lines
            }
        }

        if (fileHadActivity)
        {
            stats.SessionCount++;
            stats.Sessions.Add(session);
        }
    }

    /// <summary>
    /// 세션 식별 정보(id·cwd·브랜치)를 줍는다. 값이 있는 나중 줄이 이긴다 — 세션 도중
    /// 브랜치를 바꿨다면 마지막 상태를 보여주는 쪽이 사용자가 기대하는 값이다.
    /// </summary>
    private static void CaptureSessionMeta(JsonElement root, SessionInfo session)
    {
        if (TryGetNonEmptyString(root, "sessionId", out var id)) session.SessionId = id;
        if (TryGetNonEmptyString(root, "cwd", out var cwd)) session.ProjectPath = cwd;
        if (TryGetNonEmptyString(root, "gitBranch", out var branch)) session.GitBranch = branch;
    }

    private static bool TryGetNonEmptyString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String) return false;

        var s = el.GetString();
        if (string.IsNullOrEmpty(s)) return false;

        value = s;
        return true;
    }

    /// <summary>
    /// 첫 사람 프롬프트를 세션 제목으로 삼는다. 도구 결과·슬래시 명령 래퍼(&lt;command-name&gt; 등)·
    /// 시스템 주입 문구는 세션을 알아보는 데 도움이 안 되므로 제목으로 쓰지 않는다.
    /// </summary>
    private static void TryCaptureTitle(JsonElement root, SessionInfo session)
    {
        if (IsTrue(root, "isMeta") || IsTrue(root, "isSidechain")) return;
        if (!root.TryGetProperty("message", out var msgEl) || msgEl.ValueKind != JsonValueKind.Object) return;
        if (!msgEl.TryGetProperty("content", out var contentEl)) return;

        string? text = null;
        if (contentEl.ValueKind == JsonValueKind.String)
        {
            text = contentEl.GetString();
        }
        else if (contentEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contentEl.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (!item.TryGetProperty("type", out var itemType) || itemType.GetString() != "text") continue;
                if (item.TryGetProperty("text", out var textEl)) { text = textEl.GetString(); break; }
            }
        }

        var title = NormalizeTitle(text);
        if (title.Length > 0) session.Title = title;
    }

    private static bool IsTrue(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

    /// <summary>제목 한 줄 정규화 — 줄바꿈·연속 공백을 접고, 너무 길면 자른다.</summary>
    private static string NormalizeTitle(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";

        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length == 0 || flat[0] == '<') return "";

        return flat.Length <= MaxTitleLength ? flat : flat[..MaxTitleLength].TrimEnd() + "…";
    }
}
