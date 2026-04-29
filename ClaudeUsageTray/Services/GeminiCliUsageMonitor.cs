using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class GeminiCliUsageMonitor
{
    private readonly string _geminiDir;
    private readonly string _sessionTmpPath;

    public GeminiCliUsageMonitor()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".gemini"))
    {
    }

    public GeminiCliUsageMonitor(string geminiDir)
    {
        _geminiDir = geminiDir;
        _sessionTmpPath = Path.Combine(_geminiDir, "tmp");
    }

    public ProviderUsageSnapshot GetTodaySnapshot(long dailyTokenGoal = 50000)
    {
        var snapshot = new ProviderUsageSnapshot();

        if (!Directory.Exists(_geminiDir))
        {
            snapshot.ErrorMessage = Loc.GeminiCliSourceNotFound;
            return snapshot;
        }

        if (!Directory.Exists(_sessionTmpPath))
        {
            snapshot.ErrorMessage = Loc.GeminiCliSessionPathMissing;
            return snapshot;
        }

        var sessionFiles = Directory.EnumerateFiles(_sessionTmpPath, "session-*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTime.Date == DateTime.Now.Date || info.CreationTime.Date == DateTime.Now.Date)
            .ToList();

        snapshot.SessionCount = sessionFiles.Count;

        if (sessionFiles.Count == 0)
        {
            snapshot.ErrorMessage = Loc.GeminiCliNoUsageToday;
            snapshot.IsSubscriptionActive = false;
            return snapshot;
        }

        long totalOutputTokens = 0;
        int totalRequests = 0;
        var hourlyTokens = new long[24];

        foreach (var file in sessionFiles)
        {
            try
            {
                var (fileOutput, fileRequests) = ReadFileStats(file.FullName);
                totalOutputTokens += fileOutput;
                totalRequests += fileRequests;
                hourlyTokens[file.LastWriteTime.Hour] += fileOutput;
            }
            catch { /* Ignore locked or corrupt files */ }
        }

        snapshot.TotalOutputTokens = totalOutputTokens;
        snapshot.RequestCount = totalRequests;
        snapshot.HourlyTokens = hourlyTokens;
        snapshot.HasData = totalRequests > 0;
        snapshot.IsLimited = false;
        
        // 일일 목표량 기준 퍼센트 계산
        snapshot.ShortUsagePercent = dailyTokenGoal > 0 
            ? Math.Min(1.0, (double)totalOutputTokens / dailyTokenGoal) 
            : 0;
            
        snapshot.ErrorMessage = totalRequests > 0 ? Loc.GeminiCliEstimateOnly : Loc.GeminiCliNoUsageToday;
        snapshot.PlanType = "CLI";

        return snapshot;
    }

    private static (long outputTokens, int requestCount) ReadFileStats(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        long outputTokens = 0;
        int requestCount = 0;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("tokens", out var tokensEl) ||
                    tokensEl.ValueKind != JsonValueKind.Object) continue;
                if (!tokensEl.TryGetProperty("output", out var outputEl) ||
                    !outputEl.TryGetInt64(out var output)) continue;

                outputTokens += output;
                requestCount++;
            }
            catch { /* ignore malformed lines */ }
        }

        return (outputTokens, requestCount);
    }
}
