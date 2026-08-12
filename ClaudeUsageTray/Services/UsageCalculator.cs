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
    ///
    /// 지금이 창 밖이면 <c>null</c> — 마커를 그리지 않는다는 뜻이다. 예전에는 0~1 로 잘랐는데,
    /// 그러면 "이미 끝난 창"(리셋이 과거)이 100% 경과와 구별되지 않아 마커가 오른쪽 끝에 박혔다.
    /// 창이 끝났으면 다음 창의 어디쯤인지 알 수 없으므로 위치를 지어내지 않는다.
    /// (<see cref="FormatResetLabel"/> 도 지난 리셋에는 빈 문자열을 돌려주므로 동작이 일치한다.)
    /// </summary>
    public static double? TimeProgress(DateTimeOffset? resetAt, TimeSpan window, DateTimeOffset now)
    {
        if (resetAt is null || window.TotalSeconds <= 0) return null;
        double elapsedRatio = 1.0 - (resetAt.Value - now).TotalSeconds / window.TotalSeconds;
        // elapsedRatio > 1: 리셋이 이미 지남(창 종료). < 0: 리셋이 창 길이보다 멀리 있음(창 길이가 어긋남).
        return elapsedRatio is >= 0 and <= 1 ? elapsedRatio : null;
    }

    /// <summary>
    /// 경과 시간이 최소 기준 이상이어야 페이스(빠름/여유 판정, 초과색)를 신뢰할 수 있다고 본다.
    /// 창 초반 1~2분 사용이 "거의 전부 초과"로 과장돼 보이는 것을 막는다.
    /// 진행률을 모르면(창 밖) 페이스도 판정하지 않는다.
    /// </summary>
    public static bool IsPaceSettled(double? timeProgress, TimeSpan window, TimeSpan minElapsed)
        => timeProgress is double progress && progress * window.TotalSeconds >= minElapsed.TotalSeconds;

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
