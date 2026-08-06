using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.WeatherWarnings;

/// <summary>
/// 일본 기상청(JMA) 気象警報・注意報. API 키가 필요 없다.
/// </summary>
/// <remarks>
/// <para>
/// 문서화된 공개 API 가 아니라 기상청 방재정보 사이트가 쓰는 엔드포인트다. 예고 없이
/// 형식이 바뀔 수 있으므로 파싱 실패는 조용히 빈 목록으로 처리한다.
/// </para>
/// <para>
/// 조회 단위가 좌표가 아니라 기상대(office) 관할 구역이라 관할 전체의 특보가 함께 온다.
/// 한 관할에 注意報 가 100건 넘게 걸리는 경우가 흔해서, 알림은 警報 이상만 내보낸다.
/// </para>
/// </remarks>
public class JmaWeatherWarningProvider : IWeatherWarningProvider
{
    private const string SourceName = "JMA";

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(AppConstants.WeatherTimeoutSeconds)
    };

    /// <summary>해제 상태를 나타내는 status 값.</summary>
    private const string StatusCleared = "解除";

    /// <summary>
    /// 気象警報 코드. 이 목록에 없는 코드는 注意報 로 보고 알림을 내보내지 않는다.
    /// 새 코드가 생겨 警報 을 놓치는 쪽이, 注意報 를 警報 으로 오인해 알림을 쏟는 쪽보다 낫다.
    /// </summary>
    private static readonly Dictionary<string, string> WarningCodes = new()
    {
        ["02"] = "暴風雪警報",
        ["03"] = "大雨警報",
        ["04"] = "洪水警報",
        ["05"] = "暴風警報",
        ["06"] = "大雪警報",
        ["07"] = "波浪警報",
        ["08"] = "高潮警報"
    };

    /// <summary>特別警報 코드. 警報 보다 한 단계 위의 심각도로 보고한다.</summary>
    private static readonly Dictionary<string, string> EmergencyWarningCodes = new()
    {
        ["32"] = "暴風雪特別警報",
        ["33"] = "大雨特別警報",
        ["35"] = "暴風特別警報",
        ["36"] = "大雪特別警報",
        ["37"] = "波浪特別警報",
        ["38"] = "高潮特別警報"
    };

    /// <summary>
    /// 기상대 관할 구역의 대표 좌표. JMA 가 좌표→관할 조회를 제공하지 않아 최근접 매칭에 쓴다.
    /// 도도부현은 청 소재지, 홋카이도·오키나와·가고시마의 분할 구역은 관할 중심 도시 기준이다.
    /// 경계 부근에서 인접 관할로 매칭될 수 있으나, 특보 자체가 광역 단위라 실용상 문제되지 않는다.
    /// </summary>
    private static readonly (string Code, double Lat, double Lon)[] Offices =
    [
        ("011000", 45.4156, 141.6731), // 宗谷（稚内）
        ("012000", 43.7708, 142.3650), // 上川・留萌（旭川）
        ("013000", 44.0206, 144.2735), // 網走・北見・紋別（網走）
        ("014030", 42.9236, 143.1963), // 十勝（帯広）
        ("014100", 42.9849, 144.3819), // 釧路・根室（釧路）
        ("015000", 42.3153, 140.9736), // 胆振・日高（室蘭）
        ("016000", 43.0642, 141.3469), // 石狩・空知・後志（札幌）
        ("017000", 41.7687, 140.7288), // 渡島・檜山（函館）
        ("020000", 40.8244, 140.7400), // 青森
        ("030000", 39.7036, 141.1527), // 岩手（盛岡）
        ("040000", 38.2688, 140.8721), // 宮城（仙台）
        ("050000", 39.7186, 140.1024), // 秋田
        ("060000", 38.2404, 140.3633), // 山形
        ("070000", 37.7500, 140.4678), // 福島
        ("080000", 36.3418, 140.4468), // 茨城（水戸）
        ("090000", 36.5657, 139.8836), // 栃木（宇都宮）
        ("100000", 36.3907, 139.0604), // 群馬（前橋）
        ("110000", 35.8570, 139.6489), // 埼玉（さいたま）
        ("120000", 35.6047, 140.1233), // 千葉
        ("130000", 35.6895, 139.6917), // 東京
        ("140000", 35.4478, 139.6425), // 神奈川（横浜）
        ("150000", 37.9026, 139.0236), // 新潟
        ("160000", 36.6953, 137.2114), // 富山
        ("170000", 36.5947, 136.6256), // 石川（金沢）
        ("180000", 36.0652, 136.2216), // 福井
        ("190000", 35.6642, 138.5683), // 山梨（甲府）
        ("200000", 36.6513, 138.1810), // 長野
        ("210000", 35.3912, 136.7223), // 岐阜
        ("220000", 34.9769, 138.3831), // 静岡
        ("230000", 35.1802, 136.9066), // 愛知（名古屋）
        ("240000", 34.7303, 136.5086), // 三重（津）
        ("250000", 35.0045, 135.8686), // 滋賀（大津）
        ("260000", 35.0212, 135.7556), // 京都
        ("270000", 34.6863, 135.5200), // 大阪
        ("280000", 34.6913, 135.1830), // 兵庫（神戸）
        ("290000", 34.6851, 135.8048), // 奈良
        ("300000", 34.2261, 135.1675), // 和歌山
        ("310000", 35.5039, 134.2377), // 鳥取
        ("320000", 35.4723, 133.0505), // 島根（松江）
        ("330000", 34.6618, 133.9350), // 岡山
        ("340000", 34.3963, 132.4596), // 広島
        ("350000", 34.1861, 131.4705), // 山口
        ("360000", 34.0658, 134.5593), // 徳島
        ("370000", 34.3401, 134.0434), // 香川（高松）
        ("380000", 33.8417, 132.7657), // 愛媛（松山）
        ("390000", 33.5597, 133.5311), // 高知
        ("400000", 33.6064, 130.4181), // 福岡
        ("410000", 33.2494, 130.2988), // 佐賀
        ("420000", 32.7448, 129.8737), // 長崎
        ("430000", 32.7898, 130.7417), // 熊本
        ("440000", 33.2382, 131.6126), // 大分
        ("450000", 31.9111, 131.4239), // 宮崎
        ("460040", 28.3778, 129.4936), // 奄美（名瀬）
        ("460100", 31.5602, 130.5581), // 鹿児島（奄美除く）
        ("471000", 26.2124, 127.6809), // 沖縄本島（那覇）
        ("472000", 25.8290, 131.2320), // 大東島（南大東）
        ("473000", 24.8055, 125.2811), // 宮古島
        ("474000", 24.3448, 124.1572)  // 八重山（石垣）
    ];

    public bool Supports(WeatherLocation location) =>
        string.Equals(location.CountryCode, "JP", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<WeatherAlertItem>> GetActiveAlertsAsync(
        WeatherLocation location, CancellationToken ct = default)
    {
        try
        {
            var office = FindNearestOffice(location.Latitude, location.Longitude);
            var url = $"https://www.jma.go.jp/bosai/warning/data/warning/{office}.json";

            var json = await Http.GetStringAsync(url, ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var headline = root.TryGetProperty("headlineText", out var hl)
                ? hl.GetString() ?? "" : "";
            var reportedAt = root.TryGetProperty("reportDatetime", out var rd)
                && rd.GetString() is string rds
                && DateTimeOffset.TryParse(rds, CultureInfo.InvariantCulture,
                       DateTimeStyles.None, out var parsed)
                ? parsed : (DateTimeOffset?)null;

            if (!root.TryGetProperty("areaTypes", out var areaTypes)
                || areaTypes.ValueKind != JsonValueKind.Array)
                return Array.Empty<WeatherAlertItem>();

            // 같은 警報 이 여러 세부 구역에 걸리면 한 건으로 묶는다.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var alerts = new List<WeatherAlertItem>();

            foreach (var areaType in areaTypes.EnumerateArray())
            {
                if (!areaType.TryGetProperty("areas", out var areas)
                    || areas.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var area in areas.EnumerateArray())
                {
                    if (!area.TryGetProperty("warnings", out var warnings)
                        || warnings.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var warning in warnings.EnumerateArray())
                    {
                        var code = warning.TryGetProperty("code", out var c) ? c.GetString() : null;
                        if (string.IsNullOrEmpty(code)) continue;

                        var status = warning.TryGetProperty("status", out var s)
                            ? s.GetString() ?? "" : "";
                        if (status == StatusCleared) continue;

                        var (name, severity) = Classify(code);
                        if (name == null) continue;   // 注意報 이하는 알림 대상이 아니다

                        if (!seen.Add(code)) continue;

                        alerts.Add(new WeatherAlertItem(
                            SourceName,
                            $"{office}:{code}:{reportedAt?.ToUnixTimeSeconds() ?? 0}",
                            name, severity,
                            string.IsNullOrEmpty(headline) ? name : headline,
                            reportedAt, null));
                    }
                }
            }

            return alerts;
        }
        catch
        {
            return Array.Empty<WeatherAlertItem>();
        }
    }

    /// <summary>
    /// 코드를 특보명과 심각도로 옮긴다. 注意報 등 알림 대상이 아닌 코드는 name 이 null 이다.
    /// severity 는 NWS provider 와 같은 어휘(Extreme/Severe)를 쓴다.
    /// </summary>
    internal static (string? Name, string Severity) Classify(string code)
    {
        if (EmergencyWarningCodes.TryGetValue(code, out var emergency))
            return (emergency, "Extreme");

        if (WarningCodes.TryGetValue(code, out var warning))
            return (warning, "Severe");

        return (null, "");
    }

    internal static string FindNearestOffice(double lat, double lon)
    {
        var best = Offices[0].Code;
        var bestDistance = double.MaxValue;

        foreach (var (code, oLat, oLon) in Offices)
        {
            // 일본 국내 범위에서는 단순 제곱거리로 충분하다. 경도는 위도에 따라
            // 좁아지므로 cos 보정을 넣어 남북으로 긴 지역에서 어긋나지 않게 한다.
            var dLat = lat - oLat;
            var dLon = (lon - oLon) * Math.Cos(lat * Math.PI / 180.0);
            var distance = dLat * dLat + dLon * dLon;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = code;
            }
        }

        return best;
    }
}
