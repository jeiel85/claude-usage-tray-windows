using System.Globalization;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 날씨 알림의 중복 방지 키. PC 안의 발송 기록(<c>AlertCache</c>)과 여러 PC 간 중복 확인(ntfy sequence_id)이 같은 키를 쓴다.
///
/// 키에 들어가는 값은 <b>PC 마다 달라지는 값이면 안 된다</b> — 두 방향으로 어긋난다.
///  1) 서식: 문화권 설정을 따르는 보간 문자열이면 같은 위치·같은 날인데도 키가 달라 중복을 못 잡는다
///     (실측 — de-DE/fr-FR/sv-SE: 좌표가 "37,57", th-TH: 연도가 불교력 "2569", ar-SA: 히즈라력과 다른 숫자,
///     sv-SE: 음수 부호 U+2212). 모든 구성 요소를 InvariantCulture 로 만든다.
///  2) 시간대: PC 의 로컬 날짜·시각을 쓰면 서로 다른 시간대의 PC 가 <b>다른 날의 알림을 같은 사건으로</b> 착각해 정당한 알림이 삼켜진다
///     (도쿄 PC 가 LA 를 보고 LA 의 9월 29일 예보를 "9월 30일" 키로 보낸 뒤, LA PC 의 진짜 9월 30일 예보가 막힌다).
///     그래서 예보 알림은 <b>예보 자체의 날짜</b>(위치의 현지 날짜)로, 관측 기반 알림은 시간대 없는 UTC 로 키를 만든다.
/// en-US·ko-KR 에서 예보 키는 예전 문자열과 글자 하나 다르지 않아 기존 발송 기록이 그대로 유효하다.
/// </summary>
internal static class WeatherAlertKeys
{
    /// <summary>하루 예보 알림. 같은 예보 날짜·같은 위치(소수 둘째 자리)면 같은 키.</summary>
    public static string Daily(DateOnly forecastDate, double latitude, double longitude) =>
        FormattableString.Invariant($"daily:{forecastDate:yyyyMMdd}:{latitude:F2}:{longitude:F2}");

    /// <summary>비/폭염/한파/강풍 같은 조건 알림. <paramref name="window"/> 는 <see cref="Day"/> 또는 <see cref="WindWindow"/>.</summary>
    public static string Condition(string kind, string window, int threshold, double latitude, double longitude) =>
        FormattableString.Invariant($"condition:{kind}:{window}:{threshold}:{latitude:F2}:{longitude:F2}");

    /// <summary>예보 날짜를 가리키는 창(yyyyMMdd, 그레고리력). 비/폭염/한파는 "오늘 예보" 하나로 판정하므로 예보 날짜가 곧 창이다.</summary>
    public static string Day(DateOnly forecastDate) => forecastDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// 현재 관측값으로 판정하는 조건 알림의 쿨다운 창 — UTC 하루를 cooldownHours 시간 단위로 나눈 몇 번째 구간인지.
    /// 로컬 시각으로 자르면 서로 다른 시간대의 PC 에서 16시간 떨어진 두 순간이 같은 창으로 찍힐 수 있어 UTC 를 쓴다.
    /// </summary>
    public static string WindWindow(DateTimeOffset now, int cooldownHours)
    {
        var utc = now.UtcDateTime;
        return FormattableString.Invariant($"{utc:yyyyMMdd}-{utc.Hour / cooldownHours}");
    }
}
