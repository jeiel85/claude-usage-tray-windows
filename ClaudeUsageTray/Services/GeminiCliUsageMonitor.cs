using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Gemini CLI 세션 로그 파서.
/// 실제 로그 구조 (검증 완료):
///   ~/.gemini/tmp/&lt;projectName&gt;/chats/session-&lt;date&gt;-&lt;sid&gt;.jsonl
///
/// 토큰 정보가 들어 있는 줄은 "type":"gemini" 인 라인이며 다음 형태:
///   { "id":..., "timestamp":"2026-05-06T06:15:45.427Z", "type":"gemini",
///     "content":..., "thoughts":[...],
///     "tokens": { "input":18809, "output":139, "cached":7819, "thoughts":192, "tool":0, "total":19140 },
///     "model":"gemini-3-flash-preview" }
///
/// 이전 파서는 tokens.output 만 읽고 file mtime 으로 hourly bucketing 했지만(부정확),
/// 이번 버전은 메시지 단위 timestamp + 모든 토큰 타입을 정확히 집계한다.
/// </summary>
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

        var todayLocal = DateTime.Now.Date;
        long totalInput = 0, totalOutput = 0, totalCached = 0;
        int totalRequests = 0;
        int sessionCount = 0;
        var hourlyTokens = new long[24];

        // 오늘 활동이 있을 가능성이 있는 파일만 골라낸다 — 자정 직후 케이스 살리기 위해 어제부터의 파일을 후보로 잡음
        IEnumerable<FileInfo> candidates;
        try
        {
            candidates = Directory.EnumerateFiles(_sessionTmpPath, "session-*", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .Where(fi => fi.LastWriteTime >= todayLocal.AddDays(-1));
        }
        catch
        {
            candidates = Enumerable.Empty<FileInfo>();
        }

        foreach (var file in candidates)
        {
            try
            {
                var fileResult = ReadFileStats(file.FullName, todayLocal, hourlyTokens);
                if (fileResult.RequestCount > 0)
                {
                    sessionCount++;
                    totalInput += fileResult.InputTokens;
                    totalOutput += fileResult.OutputTokens;
                    totalCached += fileResult.CachedTokens;
                    totalRequests += fileResult.RequestCount;
                }
            }
            catch
            {
                // 잠긴/손상된 파일은 건너뜀
            }
        }

        if (totalRequests == 0)
        {
            snapshot.ErrorMessage = Loc.GeminiCliNoUsageToday;
            snapshot.IsSubscriptionActive = false;
            return snapshot;
        }

        snapshot.TotalInputTokens     = totalInput;
        snapshot.TotalOutputTokens    = totalOutput;
        snapshot.TotalCacheReadTokens = totalCached;   // Gemini는 cached input만 — write 캐시 개념 없음
        snapshot.RequestCount         = totalRequests;
        snapshot.SessionCount         = sessionCount;
        snapshot.HourlyTokens         = hourlyTokens;
        snapshot.HasData              = true;
        snapshot.IsLimited            = false;
        snapshot.PlanType             = "CLI";
        snapshot.ErrorMessage         = Loc.GeminiCliEstimateOnly;

        // 일일 목표량 기준 퍼센트 (참고값)
        snapshot.ShortUsagePercent = dailyTokenGoal > 0
            ? Math.Min(1.0, (double)totalOutput / dailyTokenGoal)
            : 0;

        return snapshot;
    }

    private readonly record struct FileStats(long InputTokens, long OutputTokens, long CachedTokens, int RequestCount);

    private static FileStats ReadFileStats(string filePath, DateTime todayLocal, long[] hourlyTokens)
    {
        long input = 0, output = 0, cached = 0;
        int requests = 0;

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                // type == "gemini" 라인만 토큰 들고 있음
                if (!root.TryGetProperty("type", out var typeEl) ||
                    typeEl.ValueKind != JsonValueKind.String ||
                    typeEl.GetString() != "gemini") continue;

                if (!root.TryGetProperty("tokens", out var tokensEl) ||
                    tokensEl.ValueKind != JsonValueKind.Object) continue;

                // 메시지 단위 timestamp — 이게 핵심 (이전 파서는 file mtime 사용하던 부정확 동작)
                DateTime? messageLocal = null;
                if (root.TryGetProperty("timestamp", out var tsEl) &&
                    tsEl.ValueKind == JsonValueKind.String &&
                    DateTime.TryParse(tsEl.GetString(), out var parsedTs))
                {
                    messageLocal = parsedTs.ToLocalTime();
                }

                // 오늘 데이터만 — timestamp가 없으면 보수적으로 건너뜀
                if (messageLocal is null || messageLocal.Value.Date != todayLocal) continue;

                long inp = ReadLong(tokensEl, "input");
                long outp = ReadLong(tokensEl, "output");
                long cachedTok = ReadLong(tokensEl, "cached");
                long thoughtsTok = ReadLong(tokensEl, "thoughts");
                // tool 토큰은 호출 부수효과로 보고 출력에 포함하지 않음

                // Gemini 의 reasoning("thoughts") 토큰은 사용자 시야에서는 응답 일부로 취급 — output에 합산
                long effectiveOutput = outp + thoughtsTok;

                input += inp;
                output += effectiveOutput;
                cached += cachedTok;
                requests++;

                // 시간대별 — 메시지 timestamp.LocalTime.Hour 사용
                int hour = messageLocal.Value.Hour;
                if (hour >= 0 && hour < 24)
                    hourlyTokens[hour] += inp + effectiveOutput;
            }
            catch
            {
                // 잘못된 JSON 라인은 건너뜀 (실제 로그에 가끔 섞여있음)
            }
        }

        return new FileStats(input, output, cached, requests);
    }

    private static long ReadLong(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el)) return 0;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v)) return v;
        return 0;
    }
}
