using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class SessionMonitor
{
    private static readonly string ProjectsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    private readonly Dictionary<string, long> _filePositions = new();
    private FileSystemWatcher? _watcher;
    private bool _hasChanges;

    public SessionMonitor()
    {
        StartWatcher();
    }

    private void StartWatcher()
    {
        if (!Directory.Exists(ProjectsPath)) return;

        try
        {
            _watcher = new FileSystemWatcher(ProjectsPath, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };
            _watcher.Changed += (_, _) => _hasChanges = true;
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            // Ignore watcher errors
        }
    }

    public SessionStats ScanTodayUsage()
    {
        var stats = new SessionStats();
        var today = DateTime.UtcNow.Date;

        // Check if any files changed since last scan
        bool scanAll = !_hasChanges;
        _hasChanges = false;

        if (!Directory.Exists(ProjectsPath)) return stats;

        var jsonlFiles = Directory.GetFiles(ProjectsPath, "*.jsonl", SearchOption.AllDirectories);

        foreach (var file in jsonlFiles)
        {
            try
            {
                if (scanAll)
                {
                    // Full scan: process entire file
                    ProcessFile(file, today, stats);
                    _filePositions[file] = new FileInfo(file).Length;
                }
                else
                {
                    // Incremental scan: only new content
                    var lastPos = _filePositions.GetValueOrDefault(file, 0);
                    ProcessFileIncremental(file, today, stats, lastPos);
                }
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return stats;
    }

    private void ProcessFileIncremental(string filePath, DateTime sinceDate, SessionStats stats, long startPosition)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var currentPos = fileInfo.Length;

            if (currentPos <= startPosition)
            {
                // File shrunk or unchanged, do full scan
                ProcessFile(filePath, sinceDate, stats);
                _filePositions[filePath] = currentPos;
                return;
            }

            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(startPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            string? line;
            bool fileHadActivity = false;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("type", out var typeEl)) continue;
                    if (typeEl.GetString() != "assistant") continue;

                    DateTime parsedTs = default;
                    if (root.TryGetProperty("timestamp", out var tsEl) && DateTime.TryParse(tsEl.GetString(), out parsedTs))
                    {
                        if (parsedTs.ToUniversalTime().Date < sinceDate) continue;
                        fileHadActivity = true;
                    }

                    if (!root.TryGetProperty("message", out var msgEl) || 
                        !msgEl.TryGetProperty("usage", out var usageEl)) continue;

                    stats.TotalInputTokens += usageEl.TryGetProperty("input_tokens", out var inp) ? inp.GetInt64() : 0;
                    stats.TotalOutputTokens += usageEl.TryGetProperty("output_tokens", out var outp) ? outp.GetInt64() : 0;
                    stats.TotalCacheReadTokens += usageEl.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt64() : 0;
                    stats.TotalCacheWriteTokens += usageEl.TryGetProperty("cache_creation_input_tokens", out var cw) ? cw.GetInt64() : 0;

                    if (parsedTs != default)
                        stats.HourlyTokens[parsedTs.ToLocalTime().Hour] += 
                            (usageEl.TryGetProperty("input_tokens", out var inp2) ? inp2.GetInt64() : 0) +
                            (usageEl.TryGetProperty("output_tokens", out var outp2) ? outp2.GetInt64() : 0);

                    if (root.TryGetProperty("error", out var errEl) && errEl.GetString() == "rate_limit")
                        stats.HasRateLimitHit = true;
                }
                catch { }
            }

            if (fileHadActivity) stats.SessionCount++;
            _filePositions[filePath] = currentPos;
        }
        catch
        {
            // Fallback to full scan
            ProcessFile(filePath, sinceDate, stats);
            try { _filePositions[filePath] = new FileInfo(filePath).Length; } catch { }
        }
    }

    private static void ProcessFile(string filePath, DateTime sinceDate, SessionStats stats)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        string? line;
        bool fileHadActivity = false;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // Only process assistant messages with usage data
                if (!root.TryGetProperty("type", out var typeEl)) continue;
                if (typeEl.GetString() != "assistant") continue;

                // Check timestamp
                DateTime parsedTs = default;
                if (root.TryGetProperty("timestamp", out var tsEl))
                {
                    if (DateTime.TryParse(tsEl.GetString(), out parsedTs))
                    {
                        if (parsedTs.ToUniversalTime().Date < sinceDate) continue;
                        fileHadActivity = true;
                        if (parsedTs.ToUniversalTime() > stats.LastActivity)
                            stats.LastActivity = parsedTs.ToUniversalTime();
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

        if (fileHadActivity) stats.SessionCount++;
    }
}
