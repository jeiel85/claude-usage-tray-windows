using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public ProviderUsageSnapshot GetTodaySnapshot()
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
            return snapshot;
        }

        long totalTokens = 0;
        var hourlyTokens = new long[24];

        foreach (var file in sessionFiles)
        {
            try
            {
                var fileTokens = ReadFileTokens(file.FullName);
                if (fileTokens <= 0) continue;

                totalTokens += fileTokens;
                // LastWriteTime 또는 파일명에 포함된 시간 중 더 정확한 것 사용 (여기서는 LastWriteTime 유지)
                hourlyTokens[file.LastWriteTime.Hour] += fileTokens;
            }
            catch { /* Ignore locked or corrupt files */ }
        }

        // Gemini CLI 무료 티어 기준 (임시 500,000 토큰)
        const long QuotaLimit = 500000; 
        snapshot.ShortUsagePercent = Math.Min(1.0, (double)totalTokens / QuotaLimit);
        snapshot.TotalInputTokens = totalTokens;
        snapshot.HourlyTokens = hourlyTokens;
        snapshot.HasData = totalTokens > 0;
        snapshot.IsLimited = true;
        snapshot.ErrorMessage = totalTokens > 0 ? Loc.GeminiCliEstimateOnly : Loc.GeminiCliNoUsageToday;
        
        return snapshot;
    }

    private static long ReadFileTokens(string filePath)
    {
        // 파일 잠금 방지를 위해 FileShare.ReadWrite 사용
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        long eventTokenSum = 0;
        long cumulativeTokenSum = 0;
        long? prevTotalTokens = null;

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (TryReadTokenMetrics(line, out var totalTokens, out var eventTokens))
            {
                if (totalTokens.HasValue)
                {
                    var current = totalTokens.Value;
                    // total_tokens는 누적값이므로 차이만큼만 더함
                    if (prevTotalTokens.HasValue && current >= prevTotalTokens.Value)
                        cumulativeTokenSum += current - prevTotalTokens.Value;
                    else if (!prevTotalTokens.HasValue)
                        cumulativeTokenSum += current;
                    
                    prevTotalTokens = current;
                }

                if (eventTokens.HasValue)
                {
                    eventTokenSum += eventTokens.Value;
                }
            }
        }

        return Math.Max(cumulativeTokenSum, eventTokenSum);
    }

    private static bool TryReadTokenMetrics(string line, out long? totalTokens, out long? eventTokens)
    {
        totalTokens = null;
        eventTokens = null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            // 1. 직접적인 total_tokens 찾기
            if (TryFindLong(root, "total_tokens", out var total) ||
                TryFindLong(root, "total", out total))
            {
                totalTokens = total;
            }

            // 2. tokens 객체 내부의 total 찾기 (예: {"tokens": {"total": 123}})
            if (root.TryGetProperty("tokens", out var tokensEl))
            {
                if (TryFindLong(tokensEl, "total", out var t)) totalTokens = t;
                if (TryFindLong(tokensEl, "input", out var i)) eventTokens = (eventTokens ?? 0) + i;
                if (TryFindLong(tokensEl, "output", out var o)) eventTokens = (eventTokens ?? 0) + o;
            }

            if (TryFindLong(root, "token_count", out var tokenCount) ||
                TryFindLong(root, "tokens", out tokenCount))
            {
                eventTokens = (eventTokens ?? 0) + tokenCount;
            }
        }
        catch
        {
            var regex = new Regex(@"""(total_tokens|totalTokens|token_count|tokens)""\s*:\s*(\d+)");
            var matches = regex.Matches(line);
            foreach (Match match in matches)
            {
                if (!long.TryParse(match.Groups[2].Value, out var value)) continue;
                var key = match.Groups[1].Value;
                if (key.Equals("total_tokens", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("totalTokens", StringComparison.OrdinalIgnoreCase))
                {
                    totalTokens = value;
                }
                else if (key.Equals("token_count", StringComparison.OrdinalIgnoreCase) ||
                         key.Equals("tokens", StringComparison.OrdinalIgnoreCase))
                {
                    eventTokens = value;
                }
            }
        }

        return totalTokens.HasValue || eventTokens.HasValue;
    }

    private static bool TryFindLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryConvertToLong(property.Value, out value))
                            return true;
                    }

                    if (TryFindLong(property.Value, propertyName, out value))
                        return true;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindLong(item, propertyName, out value))
                        return true;
                }
                break;
        }

        return false;
    }

    private static bool TryConvertToLong(JsonElement element, out long value)
    {
        value = 0;
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetInt64(out value),
            JsonValueKind.String => long.TryParse(element.GetString(), out value),
            _ => false
        };
    }
}
