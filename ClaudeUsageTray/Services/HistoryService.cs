using System.IO;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

public record DailyStats(
    string Date,           // yyyy-MM-dd
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int  SessionCount);

public class HistoryService
{
    private static readonly string ClaudeDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    private string _historyPath;
    private Dictionary<string, DailyStats> _data = new();

    public HistoryService()
    {
        _historyPath = BuildHistoryPath(null);
        Load();
    }

    /// <summary>
    /// 계정 전환 시 호출. orgUuid가 있으면 계정 전용 파일, 없으면 공용 파일 사용.
    /// </summary>
    public void SetOrgUuid(string? orgUuid)
    {
        _historyPath = BuildHistoryPath(orgUuid);
        _data = new();
        Load();
    }

    private static string BuildHistoryPath(string? orgUuid)
    {
        var suffix = string.IsNullOrEmpty(orgUuid)
            ? ""
            : $"-{orgUuid[..Math.Min(8, orgUuid.Length)]}"; // UUID 앞 8자로 짧게
        return Path.Combine(ClaudeDir, $"claude-usage-tray-history{suffix}.json");
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_historyPath)) return;
            var json = File.ReadAllText(_historyPath);
            _data = JsonSerializer.Deserialize<Dictionary<string, DailyStats>>(json)
                    ?? new Dictionary<string, DailyStats>();
        }
        catch { _data = new(); }
    }

    public void RecordToday(long input, long output, long cacheRead, long cacheWrite, int sessions)
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _data[today] = new DailyStats(today, input, output, cacheRead, cacheWrite, sessions);
        TrimOldEntries();
        Save();
    }

    public IReadOnlyList<DailyStats> GetLast(int days)
    {
        var cutoff = DateTime.UtcNow.AddDays(-days + 1).ToString("yyyy-MM-dd");
        return _data.Values
            .Where(s => string.Compare(s.Date, cutoff, StringComparison.Ordinal) >= 0)
            .OrderBy(s => s.Date)
            .ToList();
    }

    public void ExportCsv(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("날짜,입력 토큰,출력 토큰,캐시 읽기,캐시 쓰기,세션 수");
        foreach (var s in _data.Values.OrderBy(s => s.Date))
            sb.AppendLine($"{s.Date},{s.InputTokens},{s.OutputTokens},{s.CacheReadTokens},{s.CacheWriteTokens},{s.SessionCount}");
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private void TrimOldEntries()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd");
        foreach (var key in _data.Keys.Where(k => string.Compare(k, cutoff, StringComparison.Ordinal) < 0).ToList())
            _data.Remove(key);
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_historyPath,
                JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
