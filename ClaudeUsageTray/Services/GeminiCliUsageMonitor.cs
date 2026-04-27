using System.IO;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class GeminiCliUsageMonitor
{
    private static readonly string GeminiDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini");

    private static readonly string SessionTmpPath = Path.Combine(GeminiDir, "tmp");

    public ProviderUsageSnapshot GetTodaySnapshot()
    {
        var snapshot = new ProviderUsageSnapshot();

        if (!Directory.Exists(GeminiDir))
        {
            snapshot.ErrorMessage = Loc.GeminiCliSourceNotFound;
            return snapshot;
        }

        if (!Directory.Exists(SessionTmpPath))
        {
            snapshot.ErrorMessage = Loc.GeminiCliSessionPathMissing;
            return snapshot;
        }

        var sessionFiles = Directory.GetFiles(SessionTmpPath, "session-*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .Where(info => info.LastWriteTime.Date == DateTime.Now.Date)
            .ToList();

        snapshot.SessionCount = sessionFiles.Count;

        if (sessionFiles.Count == 0)
        {
            snapshot.ErrorMessage = Loc.GeminiCliNoUsageToday;
            return snapshot;
        }

        long totalTokens = 0;
        var hourlyTokens = new long[24];

        foreach (var file in sessionFiles)
        {
            try
            {
                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.Contains("\"token_count\":") || line.Contains("\"tokens\":"))
                    {
                        // 단순 텍스트 파싱 (성능 및 의존성 고려)
                        var match = System.Text.RegularExpressions.Regex.Match(line, @"\""token_count\"":\s*(\d+)");
                        if (!match.Success) match = System.Text.RegularExpressions.Regex.Match(line, @"\""tokens\"":\s*(\d+)");
                        
                        if (match.Success && long.TryParse(match.Groups[1].Value, out long tokens))
                        {
                            totalTokens += tokens;
                            int hour = file.LastWriteTime.Hour;
                            hourlyTokens[hour] += tokens;
                        }
                    }
                }
            }
            catch { /* Ignore locked or corrupt files */ }
        }

        // Gemini CLI 무료 티어 기준 (임시 500,000 토큰, 필요시 조정 가능)
        const long QuotaLimit = 500000; 
        snapshot.ShortUsagePercent = Math.Min(1.0, (double)totalTokens / QuotaLimit);
        snapshot.TotalInputTokens = totalTokens;
        snapshot.HourlyTokens = hourlyTokens;
        snapshot.HasData = true;
        snapshot.IsLimited = true;
        snapshot.ErrorMessage = totalTokens > 0 ? Loc.GeminiCliEstimateOnly : Loc.GeminiCliNoUsageToday;
        
        return snapshot;
    }
}
