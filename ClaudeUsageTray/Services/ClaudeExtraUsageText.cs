using System.Globalization;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services;

/// <summary>
/// Claude 추가 사용(<c>extra_usage</c>) 줄의 표시 규칙(#351).
/// 금액은 통화 최소 단위로 오므로(2300 = $23.00) 통화가 있으면 금액으로, 없으면(옛 응답) 크레딧 개수로 그린다.
/// </summary>
public static class ClaudeExtraUsageText
{
    /// <summary>
    /// 줄을 보일지. 켜져 있거나, 한 번이라도 켰던 계정이 지금 꺼져 있을 때(왜 꺼졌는지 알려야 한다).
    /// 추가 사용을 써 본 적 없는 계정에는 보이지 않는다 — 대부분의 사용자에게는 의미 없는 줄이다.
    /// </summary>
    public static bool IsVisible(ExtraUsage? extra) => extra is { IsEnabled: true } or { CreditsEverEnabled: true };

    /// <summary>
    /// 오른쪽 문구 — "$0.00 / $23.00", 꺼져 있으면 " · 크레딧 없음" 처럼 이유를 붙인다.
    /// 보이지 않을 줄이면 빈 문자열.
    /// </summary>
    public static string Describe(ExtraUsage? extra)
    {
        if (!IsVisible(extra)) return "";

        var amount = DescribeAmount(extra!);
        if (extra!.IsEnabled) return amount;

        var reason = DescribeDisabledReason(extra);
        return string.IsNullOrEmpty(amount) ? reason : $"{amount} · {reason}";
    }

    /// <summary>
    /// 게이지 값(0..1) — 사용액 ÷ 월 한도. 켜짐·꺼짐 모두 이 계산 하나만 쓴다(상태만 바뀌었는데 막대가 달라지지 않게).
    /// 사용액이 없으면 서버 <c>utilization</c>(%)으로. 한도가 없으면 비율이 없으므로 0(예전 동작과 같음).
    /// </summary>
    public static double Percent(ExtraUsage? extra)
    {
        if (extra is not { MonthlyLimit: > 0 }) return 0;
        if (extra.UsedCredits is { } used)
            return Math.Clamp(used / extra.MonthlyLimit.Value, 0, 1);
        return Math.Clamp((extra.Utilization ?? 0) / 100.0, 0, 1);
    }

    internal static string DescribeAmount(ExtraUsage extra)
    {
        if (string.IsNullOrWhiteSpace(extra.Currency))
        {
            // 통화 정보가 없는 옛 응답 — 지금까지처럼 크레딧 개수로.
            return (extra.UsedCredits, extra.MonthlyLimit) switch
            {
                ({ } used, { } limit) => Loc.ExtraCredits(used, limit),
                ({ } used, null) => Loc.ExtraCreditsUsedOnly(used),
                _ => "",
            };
        }

        return (extra.UsedCredits, extra.MonthlyLimit) switch
        {
            ({ } used, { } limit) => $"{FormatMoney(used, extra)} / {FormatMoney(limit, extra)}",
            ({ } used, null) => Loc.ExtraSpendUsedOnly(FormatMoney(used, extra)),
            (null, { } limit) => $"{FormatMoney(0, extra)} / {FormatMoney(limit, extra)}",
            _ => "",
        };
    }

    /// <summary>최소 단위 값을 통화 금액으로 — USD 는 "$23.00", 그 밖은 "23.00 EUR".</summary>
    internal static string FormatMoney(double minorUnits, ExtraUsage extra) =>
        FormatMoney(minorUnits, extra.Currency!, extra.DecimalPlaces ?? 2);

    /// <summary>같은 규칙의 통화 금액 — Console 크레딧(#354)도 쓴다.</summary>
    internal static string FormatMoney(double minorUnits, string currencyCode, int decimalPlaces)
    {
        var places = Math.Clamp(decimalPlaces, 0, 6);
        var value = minorUnits / Math.Pow(10, places);
        var number = value.ToString("N" + places, CultureInfo.InvariantCulture);
        var currency = currencyCode.Trim().ToUpperInvariant();
        return currency == "USD" ? $"${number}" : $"{number} {currency}";
    }

    /// <summary>Console 크레딧 줄 문구(#354) — "$57.80 남음 / $100.00 · 12-10 만료".</summary>
    public static string DescribeConsoleCredits(ClaudeConsoleCredits credits)
    {
        var amount = Loc.ConsoleCreditsAmount(
            FormatMoney(credits.RemainingMinor, credits.Currency, credits.Exponent),
            FormatMoney(credits.GrantedMinor, credits.Currency, credits.Exponent));
        return credits.ExpiresAt is { } expires
            ? $"{amount} · {Loc.ConsoleCreditsExpires(expires.ToLocalTime().ToString("MM-dd", CultureInfo.InvariantCulture))}"
            : amount;
    }

    internal static string DescribeDisabledReason(ExtraUsage extra)
    {
        if (string.Equals(extra.DisabledReason, "out_of_credits", StringComparison.OrdinalIgnoreCase))
            return Loc.ExtraUsageOutOfCredits;
        if (extra.SpendLimitReached)
            return Loc.ExtraUsageLimitReached;
        return Loc.ExtraUsageOff;
    }
}
