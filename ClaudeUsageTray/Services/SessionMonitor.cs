using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Timers;
using ClaudeUsageTray.Models;
using Timer = System.Timers.Timer;

namespace ClaudeUsageTray.Services;

public class SessionMonitor : IDisposable
{
    private static readonly string ProjectsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    private readonly ConcurrentDictionary<string, DateTime> _fileLastModified = new();
    private readonly ConcurrentDictionary<string, SessionStats> _fileStats = new();
    private FileSystemWatcher? _watcher;
    private readonly Timer _debounceTimer;
    private readonly HashSet<string> _pendingFiles = new();
    private readonly object _pendingLock = new();

    public event EventHandler? UsageChanged;

    public SessionMonitor()
    {
        // Debounce timer for file changes
        _debounceTimer = new Timer(AppConstants.FileWriteDebounceMs);
        _debounceTimer.Elapsed += OnDebounceElapsed;
        _debounceTimer.AutoReset = false;
    }

    public void StartWatching()
    {
        if (!Directory.Exists(ProjectsPath)) return;

        _watcher = new FileSystemWatcher(ProjectsPath)
        {
            Filter = "*.jsonl",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_pendingLock)
        {
            _pendingFiles.Add(e.FullPath);
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private async void OnDebounceElapsed(object? sender, ElapsedEventArgs e)
    {
        HashSet<string> filesToUpdate;
        lock (_pendingLock)
        {
            filesToUpdate = new HashSet<string>(_pendingFiles);
            _pendingFiles.Clear();
        }

        await Task.Run(() =>
        {
            foreach (var file in filesToUpdate)
            {
                if (!File.Exists(file)) continue;

                var lastMod = File.GetLastWriteTime(file);
                if (_fileLastModified.TryGetValue(file, out var cached) && cached == lastMod)
                    continue;

                _fileLastModified[file] = lastMod;
                var stats = ScanFileIncremental(file);
                _fileStats[file] = stats;
            }
        });

        UsageChanged?.Invoke(this, EventArgs.Empty);
    }

    private static SessionStats ScanFileIncremental(string filePath)
    {
        var stats = new SessionStats();
        ProcessFile(filePath, DateTime.UtcNow.Date, stats);
        return stats;
    }

    /// <summary>
    /// Get aggregated session stats from cache (fast, no I/O)
    /// </summary>
    public SessionStats GetCachedStats()
    {
        var result = new SessionStats();
        foreach (var (_, stats) in _fileStats)
        {
            result.TotalInputTokens += stats.TotalInputTokens;
            result.TotalOutputTokens += stats.TotalOutputTokens;
            result.TotalCacheReadTokens += stats.TotalCacheReadTokens;
            result.TotalCacheWriteTokens += stats.TotalCacheWriteTokens;
            result.SessionCount += stats.SessionCount;
            if (stats.LastActivity > result.LastActivity)
                result.LastActivity = stats.LastActivity;
            result.HasRateLimitHit |= stats.HasRateLimitHit;
            if (!string.IsNullOrEmpty(stats.RateLimitResetTime))
                result.RateLimitResetTime = stats.RateLimitResetTime;

            for (int i = 0; i < 24; i++)
                result.HourlyTokens[i] += stats.HourlyTokens[i];
        }
        return result;
    }

    /// <summary>
    /// Full scan - use on startup or periodically
    /// </summary>
    public SessionStats ScanTodayUsage()
    {
        var result = new SessionStats();

        if (!Directory.Exists(ProjectsPath)) return result;

        var jsonlFiles = Directory.GetFiles(ProjectsPath, "*.jsonl", SearchOption.AllDirectories);

        foreach (var file in jsonlFiles)
        {
            try
            {
                var lastMod = File.GetLastWriteTime(file);
                _fileLastModified[file] = lastMod;

                var stats = new SessionStats();
                ProcessFile(file, DateTime.UtcNow.Date, stats);
                _fileStats[file] = stats;

                result.TotalInputTokens += stats.TotalInputTokens;
                result.TotalOutputTokens += stats.TotalOutputTokens;
                result.TotalCacheReadTokens += stats.TotalCacheReadTokens;
                result.TotalCacheWriteTokens += stats.TotalCacheWriteTokens;
                result.SessionCount += stats.SessionCount;
                if (stats.LastActivity > result.LastActivity)
                    result.LastActivity = stats.LastActivity;
                result.HasRateLimitHit |= stats.HasRateLimitHit;
                if (!string.IsNullOrEmpty(stats.RateLimitResetTime))
                    result.RateLimitResetTime = stats.RateLimitResetTime;

                for (int i = 0; i < 24; i++)
                    result.HourlyTokens[i] += stats.HourlyTokens[i];
            }
            catch
            {
                // Skip unreadable files
            }
        }

        return result;
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

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer.Dispose();
    }
}
