using System.Globalization;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 날씨 알림의 중복 방지 키. PC 안의 발송 기록(<c>AlertCache</c>)과 여러 PC 간 중복 확인(ntfy sequence_id)이 같은 키를 쓴다.
///
/// 왜 따로 뽑았는가: 키를 문화권 설정을 따르는 보간 문자열로 만들면 같은 위치·같은 날인데도 PC 의 지역 설정에 따라
/// 키가 달라져 다른 PC 와의 중복을 못 잡는다(실측 — de-DE/fr-FR/sv-SE: 좌표가 "37,57", th-TH: 연도가 불교력 "2569",
/// ar-SA: 히즈라력과 다른 숫자, sv-SE: 음수 부호 U+2212). 모든 구성 요소를 InvariantCulture 로 만든다.
/// en-US·ko-KR 처럼 이미 '.' 을 쓰는 환경에서는 예전 문자열과 글자 하나 다르지 않아 기존 발송 기록이 그대로 유효하다.
/// </summary>
internal static class WeatherAlertKeys
{
    /// <summary>하루 예보 알림. 같은 날 같은 위치(소수 둘째 자리)면 같은 키.</summary>
    public static string Daily(DateTimeOffset now, double latitude, double longitude) =>
        FormattableString.Invariant($"daily:{now:yyyyMMdd}:{latitude:F2}:{longitude:F2}");

    /// <summary>비/폭염/한파/강풍 같은 조건 알림. <paramref name="window"/> 는 하루(<see cref="Day"/>) 또는 <see cref="WindWindow"/>.</summary>
    public static string Condition(string kind, string window, int threshold, double latitude, double longitude) =>
        FormattableString.Invariant($"condition:{kind}:{window}:{threshold}:{latitude:F2}:{longitude:F2}");

    /// <summary>"오늘" 을 가리키는 창(yyyyMMdd, 그레고리력).</summary>
    public static string Day(DateTimeOffset now) => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>현재 관측값으로 판정하는 조건 알림의 쿨다운 창 — 하루를 cooldownHours 시간 단위로 나눈 몇 번째 구간인지.</summary>
    public static string WindWindow(DateTimeOffset now, int cooldownHours) =>
        FormattableString.Invariant($"{now:yyyyMMdd}-{now.Hour / cooldownHours}");
}
