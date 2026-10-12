using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Claude 사용량 응답에서 특정 모델에만 걸리는 한도(#187)를 모은다 — 기본 5시간·7일 게이지와 겹치지 않는 것만.
///
/// 두 곳에서 온다.
/// <list type="bullet">
/// <item><c>limits[]</c> 의 모델 범위 항목: <c>{kind, group, percent(0~100), resets_at, scope.model.display_name, is_active}</c>.
/// 프로모션 모델(예: Fable) 같은 한도는 여기로만 온다.</item>
/// <item>옛 자리 <c>seven_day_opus</c> / <c>seven_day_sonnet</c>: 2026-09 실측 응답에서도 Sonnet 한도가
/// <c>limits[]</c> 에 대응 항목 없이 여기로만 왔다 — 둘은 중복이 아니라 서로를 보완한다.</item>
/// </list>
/// 같은 모델이 양쪽에 다 오면 서버 표시 이름이 있는 <c>limits[]</c> 쪽 하나만 쓴다.
/// 필드 의미는 공개 클라이언트 두 곳(steipete/CodexBar <c>ClaudeScopedWeeklyLimitMapper</c>,
/// jens-duttke/usage-monitor-for-claude <c>_merge_scoped_limits</c>)과 교차 확인했다.
/// </summary>
public static class ClaudeModelLimits
{
    private const int SessionWindowMinutes = 5 * 60;
    private const int WeeklyWindowMinutes = 7 * 24 * 60;

    // 옛 필드는 모델 이름을 주지 않는다 — 모델 이름은 고유명사라 번역하지 않는다.
    private const string OpusName = "Opus";
    private const string SonnetName = "Sonnet";

    public static List<AdditionalQuotaWindow> Read(UsageResponse usage, DateTimeOffset observedAt)
    {
        var windows = new List<AdditionalQuotaWindow>();
        if (usage.Limits.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in usage.Limits.EnumerateArray())
            {
                if (TryReadScopedLimit(entry, observedAt) is { } window &&
                    !windows.Any(existing => string.Equals(existing.LimitId, window.LimitId, StringComparison.OrdinalIgnoreCase)))
                {
                    windows.Add(window);
                }
            }
        }

        AddLegacyWeeklyLimit(windows, "seven_day_opus", OpusName, usage.SevenDayOpus, observedAt);
        AddLegacyWeeklyLimit(windows, "seven_day_sonnet", SonnetName, usage.SevenDaySonnet, observedAt);
        return windows;
    }

    /// <summary>
    /// <c>limits[]</c> 항목 하나. 모델 범위가 없는 항목(<c>session</c> / <c>weekly_all</c>)은 기본 게이지가 이미 보여주고,
    /// "All models" 범위도 기본 주간과 같은 한도라 뺀다. 문서화되지 않은 필드라 모양이 다르면 그 항목만 버린다.
    /// <c>is_active</c> 는 거르지 않는다 — 실제로 걸리는 모델 한도도 false 로 온다(CodexBar 실측).
    /// </summary>
    private static AdditionalQuotaWindow? TryReadScopedLimit(JsonElement entry, DateTimeOffset observedAt)
    {
        if (entry.ValueKind != JsonValueKind.Object)
            return null;

        var kind = ReadString(entry, "kind");
        if (kind is "session" or "weekly_all")
            return null;

        if (!entry.TryGetProperty("scope", out var scope) || scope.ValueKind != JsonValueKind.Object ||
            !scope.TryGetProperty("model", out var model) || model.ValueKind != JsonValueKind.Object)
            return null;

        var displayName = ReadString(model, "display_name")?.Trim();
        if (string.IsNullOrEmpty(displayName))
            return null;

        var modelId = ReadString(model, "id")?.Trim();
        if (IsAllModels(displayName) || (!string.IsNullOrEmpty(modelId) && IsAllModels(modelId)))
            return null;

        if (ReadPercent(entry) is not double percent)
            return null;

        var group = ReadString(entry, "group");
        var slug = Slug(string.IsNullOrEmpty(modelId) ? displayName : modelId);
        if (slug.Length == 0)
            return null;

        return new AdditionalQuotaWindow
        {
            LimitId = $"{group ?? kind ?? "limit"}:{slug}",
            LimitName = displayName,
            UsagePercent = Math.Clamp(percent / 100.0, 0, 1),
            ResetAt = ReadResetAt(entry),
            WindowMinutes = ReadWindowMinutes(group, kind),
            ObservedAt = observedAt,
        };
    }

    /// <summary>
    /// 창 길이는 <c>group</c>(weekly / session)으로 알고, 없으면 <c>kind</c> 앞부분(weekly_scoped → 주간)으로 짐작한다.
    /// 둘 다 모르면 지어내지 않는다 — 행이 이름만 쓰고 시간선을 그리지 않는다.
    /// </summary>
    private static int? ReadWindowMinutes(string? group, string? kind) => (group ?? kind) switch
    {
        "weekly" => WeeklyWindowMinutes,
        "session" => SessionWindowMinutes,
        string value when value.StartsWith("weekly_", StringComparison.Ordinal) => WeeklyWindowMinutes,
        string value when value.StartsWith("session_", StringComparison.Ordinal) => SessionWindowMinutes,
        _ => null
    };

    /// <summary>
    /// 옛 필드 하나. 같은 모델의 주간 한도가 <c>limits[]</c> 에 이미 있으면 넣지 않는다 —
    /// 서버 표시 이름이 "Sonnet 4.6" 이나 "Claude Sonnet" 이어도 같은 모델로 본다(단어 단위 비교).
    /// 창 길이를 모르는 <c>limits[]</c> 항목도 같은 모델이면 같은 한도로 본다 — 두 줄로 그리는 것보다 낫다.
    /// </summary>
    private static void AddLegacyWeeklyLimit(
        List<AdditionalQuotaWindow> windows, string limitId, string modelName, UsageWindow? window, DateTimeOffset observedAt)
    {
        if (window is null)
            return;

        if (windows.Any(existing => (existing.WindowMinutes is null or WeeklyWindowMinutes) && NamesModel(existing.LimitName, modelName)))
            return;

        windows.Add(new AdditionalQuotaWindow
        {
            LimitId = limitId,
            LimitName = modelName,
            UsagePercent = Math.Clamp(window.UsagePercent, 0, 1),
            ResetAt = window.ResetsAtParsed,
            WindowMinutes = WeeklyWindowMinutes,
            ObservedAt = observedAt,
        });
    }

    private static bool NamesModel(string displayName, string modelName) =>
        Words(displayName).Contains(modelName, StringComparer.OrdinalIgnoreCase);

    private static bool IsAllModels(string value)
    {
        var slug = Slug(value);
        return slug == "all-models" || slug.EndsWith("-all-models", StringComparison.Ordinal);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static double? ReadPercent(JsonElement entry)
    {
        if (!entry.TryGetProperty("percent", out var value))
            return null;

        double percent;
        if (value.ValueKind == JsonValueKind.Number)
            percent = value.GetDouble();
        else if (value.ValueKind != JsonValueKind.String ||
                 !double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
            return null;

        return double.IsFinite(percent) ? percent : null;
    }

    private static DateTimeOffset? ReadResetAt(JsonElement entry) =>
        ReadString(entry, "resets_at") is { } text &&
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var resetAt)
            ? resetAt
            : null;

    private static IEnumerable<string> Words(string value) =>
        Regex.Split(value, @"[^\p{L}\p{Nd}]+").Where(word => word.Length > 0);

    /// <summary>"Claude Sonnet 4.6" → "claude-sonnet-4-6". 같은 모델 항목이 두 번 오면 하나만 남기는 데 쓴다.</summary>
    private static string Slug(string value) => string.Join('-', Words(value.ToLowerInvariant()));
}
