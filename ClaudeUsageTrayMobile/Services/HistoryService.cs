using System.Text;
using System.Text.Json;

namespace ClaudeUsageTrayMobile.Services;

public record DailyStats(
    string Date,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int  SessionCount);

/// <summary>
/// Tracks daily API usage history in app's local data directory.
/// On Android: /data/data/{packageId}/files/
/// On iOS: NSDocumentDirectory
/// </summary>
public class HistoryService
{
    private static readonly string HistoryPath = Path.Combine(
        FileSystem.AppDataDirectory, "claude-usage-history.json");

    private Dictionary<string, DailyStats> _data = new();

    public HistoryService() => Load();

    private void Load()
    {
        try
        {
            if (!File.Exists(HistoryPath)) return;
            var json = File.ReadAllText(HistoryPath);
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

    public string ExportCsvContent()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Date,Input Tokens,Output Tokens,Cache Read,Cache Write,Sessions");
        foreach (var s in _data.Values.OrderBy(s => s.Date))
            sb.AppendLine($"{s.Date},{s.InputTokens},{s.OutputTokens},{s.CacheReadTokens},{s.CacheWriteTokens},{s.SessionCount}");
        return sb.ToString();
    }

    private void TrimOldEntries()
    {
        var cutoff = DateTime.UtcNow.AddDays(-90).ToString("yyyy-MM-dd");
        foreach (var key in _data.Keys
            .Where(k => string.Compare(k, cutoff, StringComparison.Ordinal) < 0).ToList())
            _data.Remove(key);
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(HistoryPath,
                JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
