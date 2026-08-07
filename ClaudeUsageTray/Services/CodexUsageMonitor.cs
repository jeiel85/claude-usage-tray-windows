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
            };
            ApplyRateLimits(rateLimitsEl, snapshot);
            // API 응답이라고 리셋 시각이 항상 미래인 것은 아니다(캐시된 응답·시계 어긋남).
            DropExpiredWindows(snapshot, DateTimeOffset.Now);
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

    // sessionsPath / now 를 주입받는 오버로드 — 단위 테스트에서 임시 로그 폴더와 기준 시각을 지정하기 위한 것.
    public ProviderUsageSnapshot GetTodaySnapshot(string sessionsPath, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.Now;
        var snapshot = new ProviderUsageSnapshot();
        if (!Directory.Exists(sessionsPath))
        {
            snapshot.ErrorMessage = Loc.CodexSourceNotFound;
            return snapshot;
        }

        var today = reference.LocalDateTime.Date;
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

        DropExpiredWindows(snapshot, reference);

        if (!snapshot.HasData && snapshot.ErrorMessage is null)
            snapshot.ErrorMessage = Loc.CodexNoUsageToday;

        return snapshot;
    }

    /// <summary>
    /// 리셋 시각이 이미 지난 창은 통째로 버린다.
    /// 로그 스캔은 날짜와 무관하게 가장 최근의 rate_limits 를 집어오므로, 며칠 전 세션 로그에서
    /// 이미 끝난 창의 사용률과 리셋 시각을 그대로 읽어 온다. 그 값은 지금 창을 설명하지 않는다.
    /// 남겨 두면 사용률은 옛날 숫자로 표시되고 시간선은 100% 경과로 계산돼 막대 오른쪽 끝에 박힌다.
    /// (동기화가 켜져 있으면 MainViewModel 이 다른 PC 의 최신 할당량으로 이 빈자리를 채운다.)
    /// </summary>
    private static void DropExpiredWindows(ProviderUsageSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.ShortResetAt is { } shortReset && shortReset <= now)
        {
            snapshot.ShortUsagePercent = 0;
            snapshot.ShortResetAt = null;
            snapshot.ShortWindowMinutes = null;
            snapshot.IsShortResetEstimated = false;
        }

        if (snapshot.LongResetAt is { } longReset && longReset <= now)
        {
            snapshot.LongUsagePercent = 0;
            snapshot.LongResetAt = null;
            snapshot.LongWindowMinutes = null;
        }
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

                // 이름 그대로 "오늘 첫 활동" 이어야 한다. 날짜를 안 보면 며칠 전 로그의 첫 줄이 잡혀
                // 리셋 추정치가 과거로 계산되고, 시간선이 100% 경과(막대 오른쪽 끝)로 고정된다.
                if (ts != DateTimeOffset.MinValue && ts.ToLocalTime().Date == today)
                    firstTodayActivityTs ??= ts;

                if (payloadEl.TryGetProperty("rate_limits", out var rateLimitsEl) && ts > latestRateTs)
                {
                    latestRateTs = ts;
                    ApplyRateLimits(rateLimitsEl, snapshot);
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

        // 로그에 resets_at이 없으면 금번 초기화 전 첫 활동 시간 + 창 길이로 폴백(창 길이를 알 때만).
        // 창 길이를 모르면 임의 추정 대신 표시하지 않는다(예전 5시간 하드코딩은 주간 창에 맞지 않았음).
        if (snapshot.ShortResetAt is null && firstTodayActivityTs.HasValue &&
            snapshot.ShortWindowMinutes is int shortWindowMinutes && shortWindowMinutes > 0)
        {
            snapshot.ShortResetAt = firstTodayActivityTs.Value.AddMinutes(shortWindowMinutes);
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

    // rate_limits의 primary/secondary 창을 window_minutes 오름차순으로 정렬해
    // 짧은 창을 Short 슬롯, 긴 창을 Long 슬롯에 배치한다. 백엔드가 슬롯에 담는 창
    // 길이를 바꿔도(예: primary가 5시간→주간) 라벨/위치가 어긋나지 않는다.
    private static void ApplyRateLimits(JsonElement rateLimitsEl, ProviderUsageSnapshot snapshot)
    {
        var windows = new List<RateWindow>(2);
        if (ReadWindow(rateLimitsEl, "primary") is { } primary) windows.Add(primary);
        if (ReadWindow(rateLimitsEl, "secondary") is { } secondary) windows.Add(secondary);

        // window_minutes 오름차순(길이를 모르는 창은 뒤로)
        windows.Sort((a, b) => (a.WindowMinutes ?? int.MaxValue).CompareTo(b.WindowMinutes ?? int.MaxValue));

        snapshot.ShortUsagePercent = 0;
        snapshot.ShortResetAt = null;
        snapshot.ShortWindowMinutes = null;
        snapshot.IsShortResetEstimated = false;
        snapshot.LongUsagePercent = 0;
        snapshot.LongResetAt = null;
        snapshot.LongWindowMinutes = null;

        if (windows.Count > 0)
        {
            snapshot.ShortUsagePercent = windows[0].Percent;
            snapshot.ShortResetAt = windows[0].ResetAt;
            snapshot.ShortWindowMinutes = windows[0].WindowMinutes;
        }
        if (windows.Count > 1)
        {
            snapshot.LongUsagePercent = windows[1].Percent;
            snapshot.LongResetAt = windows[1].ResetAt;
            snapshot.LongWindowMinutes = windows[1].WindowMinutes;
        }

        if (rateLimitsEl.TryGetProperty("plan_type", out var planEl) && planEl.ValueKind == JsonValueKind.String)
            snapshot.PlanType = planEl.GetString();
    }

    // 창(primary/secondary)이 객체가 아니면(JSON null 포함) null 반환.
    private static RateWindow? ReadWindow(JsonElement rateLimitsEl, string windowName)
    {
        if (!rateLimitsEl.TryGetProperty(windowName, out var windowEl) ||
            windowEl.ValueKind != JsonValueKind.Object)
            return null;

        double percent = windowEl.TryGetProperty("used_percent", out var percentEl) &&
                         percentEl.ValueKind == JsonValueKind.Number
            ? Math.Clamp(percentEl.GetDouble() / 100.0, 0, 1)
            : 0;

        // epoch 가 0/음수면 "리셋 시각 없음"이다. 그대로 받으면 1970-01-01 이 되어
        // 시간선이 100% 경과로 계산되고 막대 오른쪽 끝에 박힌다(window_minutes 처럼 양수만 받는다).
        DateTimeOffset? resetAt = windowEl.TryGetProperty("resets_at", out var resetEl) &&
                                  resetEl.TryGetInt64(out var epoch) && epoch > 0
            ? DateTimeOffset.FromUnixTimeSeconds(epoch)
            : null;

        int? windowMinutes = windowEl.TryGetProperty("window_minutes", out var wmEl) &&
                             wmEl.TryGetInt32(out var wm) && wm > 0
            ? wm
            : null;

        return new RateWindow(percent, resetAt, windowMinutes);
    }

    private readonly record struct RateWindow(double Percent, DateTimeOffset? ResetAt, int? WindowMinutes);

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
