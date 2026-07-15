using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public class CodexUsageMonitor
{
    private static readonly string SessionsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "sessions");

    private static readonly string AuthPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "auth.json");

    private const string UsageApiEndpoint = "https://api.openai-v2.com/backend-api/codex/usage";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(AppConstants.ApiTimeoutSeconds) };

    public async Task<ProviderUsageSnapshot> GetTodaySnapshotAsync(bool useDirectApi = true)
    {
        if (useDirectApi)
        {
            var apiSnapshot = await FetchUsageFromApiAsync();
            if (apiSnapshot is { HasData: true })
            {
                // API 데이터가 있으면 로그 기반 데이터와 병합하거나 API 우선 사용
                // 여기서는 API 데이터를 우선하고, 상세 히스토리(시간대별)만 로그에서 보완
                var logSnapshot = GetTodaySnapshot();
                apiSnapshot.HourlyTokens = logSnapshot.HourlyTokens;
                apiSnapshot.TotalInputTokens = logSnapshot.TotalInputTokens;
                apiSnapshot.TotalOutputTokens = logSnapshot.TotalOutputTokens;
                apiSnapshot.TotalCacheReadTokens = logSnapshot.TotalCacheReadTokens;
                apiSnapshot.SessionCount = logSnapshot.SessionCount;
                return apiSnapshot;
            }
        }

        var snapshot = GetTodaySnapshot();
        snapshot.DataSource = "Local Log";
        return snapshot;
    }

    private async Task<ProviderUsageSnapshot?> FetchUsageFromApiAsync()
    {
        try
        {
            var token = TryGetAccessToken();
            if (string.IsNullOrEmpty(token)) return null;

            var request = new HttpRequestMessage(HttpMethod.Get, UsageApiEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // API 응답 구조 파싱 (리뷰 문서의 rate_limits 구조 기반)
            if (!root.TryGetProperty("rate_limits", out var rateLimitsEl)) return null;

            var snapshot = new ProviderUsageSnapshot
            {
                HasData = true,
                DataSource = "Direct API",
                ShortUsagePercent = ReadPercent(rateLimitsEl, "primary"),
                LongUsagePercent = ReadPercent(rateLimitsEl, "secondary"),
                ShortResetAt = ReadReset(rateLimitsEl, "primary"),
                LongResetAt = ReadReset(rateLimitsEl, "secondary"),
                PlanType = rateLimitsEl.TryGetProperty("plan_type", out var planEl) ? planEl.GetString() : null
            };
            snapshot.IsSubscriptionActive = snapshot.PlanType is "Plus" or "Team" or "Enterprise";

            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetAccessToken()
    {
        try
        {
            if (!File.Exists(AuthPath)) return null;
            var json = File.ReadAllText(AuthPath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("access_token", out var tokenEl) ? tokenEl.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    public ProviderUsageSnapshot GetTodaySnapshot() => GetTodaySnapshot(SessionsPath);

    // sessionsPath 를 주입받는 오버로드 — 단위 테스트에서 임시 로그 폴더를 지정하기 위한 것.
    public ProviderUsageSnapshot GetTodaySnapshot(string sessionsPath)
    {
        var snapshot = new ProviderUsageSnapshot();
        if (!Directory.Exists(sessionsPath))
        {
            snapshot.ErrorMessage = Loc.CodexSourceNotFound;
            return snapshot;
        }

        var today = DateTime.Now.Date;
        var latestRateTs = DateTimeOffset.MinValue;
        var files = Directory.GetFiles(sessionsPath, "rollout-*.jsonl", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                ProcessFile(file, today, snapshot, ref latestRateTs);
            }
            catch
            {
                // Skip unreadable files
            }
        }

        if (!snapshot.HasData && snapshot.ErrorMessage is null)
            snapshot.ErrorMessage = Loc.CodexNoUsageToday;

        return snapshot;
    }

    private static void ProcessFile(string filePath, DateTime today, ProviderUsageSnapshot snapshot,
        ref DateTimeOffset latestRateTs)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);

        UsageTotals? prevTotals = null;
        bool fileHadTodayActivity = false;
        string? line;
        DateTimeOffset? firstTodayActivityTs = null;

        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "event_msg")
                    continue;
                if (!root.TryGetProperty("payload", out var payloadEl))
                    continue;
                if (!payloadEl.TryGetProperty("type", out var payloadTypeEl) || payloadTypeEl.GetString() != "token_count")
                    continue;

                var ts = root.TryGetProperty("timestamp", out var tsEl) &&
                         DateTimeOffset.TryParse(tsEl.GetString(), out var parsedTs)
                    ? parsedTs
                    : DateTimeOffset.MinValue;

                if (ts != DateTimeOffset.MinValue)
                    firstTodayActivityTs ??= ts;

                if (payloadEl.TryGetProperty("rate_limits", out var rateLimitsEl) && ts > latestRateTs)
                {
                    latestRateTs = ts;
                    snapshot.ShortUsagePercent = ReadPercent(rateLimitsEl, "primary");
                    snapshot.LongUsagePercent = ReadPercent(rateLimitsEl, "secondary");
                    snapshot.ShortResetAt = ReadReset(rateLimitsEl, "primary");
                    snapshot.IsShortResetEstimated = false;
                    snapshot.LongResetAt = ReadReset(rateLimitsEl, "secondary");
                    snapshot.PlanType = rateLimitsEl.TryGetProperty("plan_type", out var planEl)
                        ? planEl.GetString()
                        : snapshot.PlanType;
                }

                if (!TryReadTotals(payloadEl, out var totals))
                    continue;

                if (ts != DateTimeOffset.MinValue && ts.ToLocalTime().Date == today)
                {
                    var delta = prevTotals is null ? totals : totals.Subtract(prevTotals.Value);
                    snapshot.TotalInputTokens += Math.Max(0, delta.InputTokens);
                    snapshot.TotalOutputTokens += Math.Max(0, delta.OutputTokens + delta.ReasoningOutputTokens);
                    snapshot.TotalCacheReadTokens += Math.Max(0, delta.CachedInputTokens);
                    snapshot.TotalCacheWriteTokens += 0;
                    snapshot.HourlyTokens[ts.ToLocalTime().Hour] += Math.Max(0, delta.TotalTokens);
                    fileHadTodayActivity = true;
                    snapshot.HasData = true;
                }

                prevTotals = totals;
            }
            catch
            {
                // Skip malformed lines
            }
        }

        if (fileHadTodayActivity)
            snapshot.SessionCount++;

        // 로그에 resets_at이 없으면 금번 초기화 전 첫 활동 시간 + 5시간으로 폴백
        if (snapshot.ShortResetAt is null && firstTodayActivityTs.HasValue)
        {
            snapshot.ShortResetAt = firstTodayActivityTs.Value.AddHours(5);
            snapshot.IsShortResetEstimated = true;
        }
    }

    private static bool TryReadTotals(JsonElement payloadEl, out UsageTotals totals)
    {
        totals = default;
        if (!payloadEl.TryGetProperty("info", out var infoEl) ||
            infoEl.ValueKind == JsonValueKind.Null ||
            !infoEl.TryGetProperty("total_token_usage", out var totalEl))
            return false;

        totals = new UsageTotals(
            ReadLong(totalEl, "input_tokens"),
            ReadLong(totalEl, "cached_input_tokens"),
            ReadLong(totalEl, "output_tokens"),
            ReadLong(totalEl, "reasoning_output_tokens"),
            ReadLong(totalEl, "total_tokens"));
        return true;
    }

    private static double ReadPercent(JsonElement rateLimitsEl, string windowName)
    {
        // 백엔드가 창(primary/secondary)을 JSON null로 내려보내면 windowEl.ValueKind == Null 이 되고,
        // 이 상태에서 TryGetProperty/GetDouble 을 호출하면 InvalidOperationException 이 발생한다.
        // 창이 객체이고 used_percent 가 숫자일 때만 읽는다.
        if (!rateLimitsEl.TryGetProperty(windowName, out var windowEl) ||
            windowEl.ValueKind != JsonValueKind.Object ||
            !windowEl.TryGetProperty("used_percent", out var percentEl) ||
            percentEl.ValueKind != JsonValueKind.Number)
            return 0;

        return Math.Clamp(percentEl.GetDouble() / 100.0, 0, 1);
    }

    private static DateTimeOffset? ReadReset(JsonElement rateLimitsEl, string windowName)
    {
        if (!rateLimitsEl.TryGetProperty(windowName, out var windowEl) ||
            windowEl.ValueKind != JsonValueKind.Object ||
            !windowEl.TryGetProperty("resets_at", out var resetEl))
            return null;

        return resetEl.TryGetInt64(out var epoch)
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;
    }

    // TryGetInt64 는 숫자가 아니면(null 등) 예외 대신 false 를 반환하므로 GetInt64 보다 안전하다.
    private static long ReadLong(JsonElement el, string name) =>
        el.TryGetProperty(name, out var valueEl) && valueEl.TryGetInt64(out var value) ? value : 0;

    private readonly record struct UsageTotals(
        long InputTokens,
        long CachedInputTokens,
        long OutputTokens,
        long ReasoningOutputTokens,
        long TotalTokens)
    {
        public UsageTotals Subtract(UsageTotals other) => new(
            InputTokens - other.InputTokens,
            CachedInputTokens - other.CachedInputTokens,
            OutputTokens - other.OutputTokens,
            ReasoningOutputTokens - other.ReasoningOutputTokens,
            TotalTokens - other.TotalTokens);
    }
}
