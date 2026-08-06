using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

public static class UsageCalculator
{
    public static string CalcDepletionLabel(UsageWindow w, DateTimeOffset now)
    {
        if (w.ResetsAtParsed is null || w.UsagePercent <= 0.02 || w.UsagePercent >= 1.0) return "";

        var windowStart = w.ResetsAtParsed.Value - TimeSpan.FromHours(5);
        var elapsed = now - windowStart;
        if (elapsed.TotalMinutes < 5) return "";

        double ratePerHour = w.UsagePercent / elapsed.TotalHours;
        if (ratePerHour <= 0) return "";

        double hoursToFull = (1.0 - w.UsagePercent) / ratePerHour;
        var remaining = w.ResetsAtParsed.Value - now;

        if (hoursToFull >= remaining.TotalHours) return "";

        var depletionAt = now.AddHours(hoursToFull).ToLocalTime();
        return Loc.DepletionAt(depletionAt.ToString("HH:mm"));
    }

    public static string CalcLongDepletionLabel(UsageWindow w, DateTimeOffset now)
    {
        if (w.ResetsAtParsed is null || w.UsagePercent <= 0.02 || w.UsagePercent >= 1.0) return "";

        var windowStart = w.ResetsAtParsed.Value - TimeSpan.FromDays(7);
        var elapsed = now - windowStart;
        if (elapsed.TotalHours < 2) return "";

        double ratePerDay = w.UsagePercent / elapsed.TotalDays;
        if (ratePerDay <= 0) return "";

        double daysToFull = (1.0 - w.UsagePercent) / ratePerDay;
        var remaining = w.ResetsAtParsed.Value - now;

        if (daysToFull >= remaining.TotalDays) return "";

        var depletionAt = now.AddDays(daysToFull).ToLocalTime();
        var timeStr = daysToFull < 1
            ? depletionAt.ToString("HH:mm")
            : depletionAt.ToString("M/d HH:mm");
        return Loc.DepletionAt(timeStr);
    }

    public static string CalcCostLabel(long input, long output, long cacheRead, long cacheWrite)
    {
        var cost = input * 3e-6
                 + output * 15e-6
                 + cacheRead * 0.3e-6
                 + cacheWrite * 3.75e-6;
        if (cost < 0.001) return "";
        return Loc.CostEstimate(cost);
    }

    public static string FormatResetLabel(DateTimeOffset? resetAt, bool isEstimated, bool showAbsolute, DateTimeOffset now)
    {
        if (resetAt is null) return "";
        var diff = resetAt.Value - now;
        if (diff.TotalSeconds <= 0) return "";
        string time;
        if (diff.TotalMinutes < 10) time = $"{(int)diff.TotalMinutes}m {diff.Seconds:D2}s";
        else if (diff.TotalHours < 1) time = $"{(int)diff.TotalMinutes}m";
        else if (diff.TotalDays < 1) time = $"{(int)diff.TotalHours}h {diff.Minutes}m";
        else time = $"{(int)diff.TotalDays}d {diff.Hours}h";
        var rel = isEstimated ? Loc.ResetsInEstimated(time) : Loc.ResetsIn(time);
        if (!showAbsolute) return rel;

        var local = resetAt.Value.ToLocalTime();
        var stamp = local.Date == now.LocalDateTime.Date
            ? local.ToString("HH:mm")
            : local.ToString("MM/dd HH:mm");
        return $"{rel} ({stamp})";
    }

    /// <summary>
    /// 리셋 시각과 창 길이로 "시간 진행률"(0~1)을 역산한다.
    /// 창 시작 = 리셋 - 창 길이 이므로, 경과 비율 = 1 - 남은시간 / 창 길이.
    /// 진행 막대 위 시간선 마커의 가로 위치가 이 값이다.
    /// </summary>
    public static double TimeProgress(DateTimeOffset? resetAt, TimeSpan window, DateTimeOffset now)
    {
        if (resetAt is null || window.TotalSeconds <= 0) return 0;
        double elapsedRatio = 1.0 - (resetAt.Value - now).TotalSeconds / window.TotalSeconds;
        return Math.Clamp(elapsedRatio, 0, 1);
    }

    /// <summary>
    /// 응답의 window_minutes 를 창 길이로 변환한다. 길이를 모르면 <paramref name="fallback"/>.
    /// Codex 는 플랜/계정에 따라 창이 5시간이 아니라 주간일 수 있어, 길이를 하드코딩하면
    /// 시간 진행률이 음수로 나와 0 으로 잘리고(=시간선이 왼쪽 끝에 붙어 안 보임) 만다.
    /// </summary>
    public static TimeSpan WindowSpan(int? windowMinutes, TimeSpan fallback) =>
        windowMinutes is int m && m > 0 ? TimeSpan.FromMinutes(m) : fallback;

    public static string FormatTokenShort(long tokens) =>
        tokens >= 1_000_000 ? $"{tokens / 1_000_000.0:F1}M" :
        tokens >= 1_000     ? $"{tokens / 1_000.0:F1}K" :
        tokens.ToString();

    public static int ThresholdToPriority(int threshold) => threshold switch
    {
        >= 100 => 5,
        >= 90  => 4,
        >= 75  => 3,
        _      => 2
    };

    public static bool IsNoUsageInformational(string? message, string providerKey)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;
        return providerKey switch
        {
            UsageProviderKind.Codex     => message == Loc.CodexNoUsageToday,
            UsageProviderKind.GeminiCli => message == Loc.GeminiCliNoUsageToday
                                       || message == Loc.GeminiCliEstimateOnly,
            UsageProviderKind.OpenCode  => message == Loc.OpenCodeNoUsageToday,
            _ => false,
        };
    }
}
