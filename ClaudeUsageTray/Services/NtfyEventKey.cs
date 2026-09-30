using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 알림 1건이 "어떤 사건인지" 를 PC·언어·조회 시각과 무관하게 식별하는 키.
/// ntfy 의 <c>sequence_id</c> 로 발행하고, 발송 전에 토픽 캐시에서 같은 키가 이미 있는지 찾는다.
///
/// 왜 본문 비교가 아니라 키인가: 예전 중복 검사는 본문(title+message)이 같은지 봤는데, 본문에는
/// PC 이름·"N분 후 초기화" 같은 PC마다 달라지는 값이 들어 있어 다른 PC 의 알림과는 절대 일치하지 않았다(#175).
/// 사건을 식별하는 값(공급자·종류·임계값·창의 리셋 시각)만 뽑아 키로 쓰면 그런 잡음이 판정에 끼지 않는다.
/// </summary>
internal sealed class NtfyEventKey
{
    // ntfy 서버의 sequenceIDRegex = ^[-_A-Za-z0-9]{1,64}$ (server/server.go).
    // 어기면 HTTP 400("sequence ID invalid") 으로 알림 자체가 거부되므로 키는 항상 이 형식을 지켜야 한다.
    private const int MaxSequenceIdLength = 64;

    // "_" 한 글자 + epoch 초 10자리. 접두사가 이보다 길면 잘라 해시로 보완해 서로 다른 키가 같아지지 않게 한다.
    private const int MaxPrefixLength = MaxSequenceIdLength - 11;

    private const int HashSuffixLength = 12;

    // 사용량 알림: 같은 창의 리셋 시각은 서버 값이라 PC 간에 사실상 같지만, 소수점 초 처리 차이를 흡수한다.
    // 서로 다른 창은 최소 수 시간 떨어져 있어 5분이면 둘을 안전하게 구분한다.
    private static readonly TimeSpan WindowResetTolerance = TimeSpan.FromMinutes(5);

    // 창 정보가 없는 알림은 "감지한 시각" 을 기준으로 삼는다. 같은 사건을 다른 PC 가 몇 분 늦게 감지해도 잡되,
    // 진짜 새 사건(새 창에서 다시 도달)은 놓치지 않을 만큼만 넓힌다 — 놓치는 쪽이 중복보다 나쁘다.
    private static readonly TimeSpan DetectionTolerance = TimeSpan.FromMinutes(30);

    // 조기 소진 예상 시각은 PC 마다 현재 시각 기준으로 계산되어 몇 분씩 어긋난다.
    private static readonly TimeSpan DepletionTolerance = TimeSpan.FromMinutes(10);

    public string Prefix { get; }
    public DateTimeOffset? At { get; }
    public TimeSpan Tolerance { get; }

    private NtfyEventKey(string prefix, DateTimeOffset? at, TimeSpan tolerance)
    {
        Prefix = Fit(prefix);
        At = at;
        Tolerance = tolerance;
    }

    /// <summary>ntfy 에 <c>sequence_id</c> 로 실어 보낼 값. 항상 <c>^[-_A-Za-z0-9]{1,64}$</c> 를 만족한다.</summary>
    public string SequenceId => At is { } at
        ? $"{Prefix}_{Math.Max(0, at.ToUnixTimeSeconds()).ToString(CultureInfo.InvariantCulture)}"
        : Prefix;

    /// <summary>
    /// 임계값 도달 알림. 창의 리셋 시각이 서버 값이면 그것으로 "같은 창" 을 가르고,
    /// 없거나 추정치면(PC 마다 달라짐) 감지 시각으로 대신한다.
    /// </summary>
    public static NtfyEventKey ForUsage(
        string agent, string windowId, int thresholdPercent, DateTimeOffset? windowResetAt, DateTimeOffset now) =>
        windowResetAt is { } reset
            ? new NtfyEventKey($"usage-{Slug(agent)}-{Slug(windowId)}-{thresholdPercent}", reset, WindowResetTolerance)
            : new NtfyEventKey($"usage-{Slug(agent)}-{Slug(windowId)}-{thresholdPercent}", now, DetectionTolerance);

    /// <summary>초기화·레이트 리밋처럼 "지금 일어난" 사건. 같은 공급자의 같은 종류는 한 번 일어나면 한동안 다시 일어나지 않는다.</summary>
    public static NtfyEventKey ForInstant(string kind, string agent, DateTimeOffset now) =>
        new($"{Slug(kind)}-{Slug(agent)}", now, DetectionTolerance);

    /// <summary>조기 소진 알림. 같은 창 안에서 예상이 앞당겨지면(=시각이 크게 달라지면) 새 사건으로 본다.</summary>
    public static NtfyEventKey ForEarlyExhaustion(string agent, DateTimeOffset? depletionAt, DateTimeOffset now) =>
        new($"early-{Slug(agent)}", depletionAt ?? now, depletionAt is null ? DetectionTolerance : DepletionTolerance);

    /// <summary>
    /// 이미 자체 중복 키가 있는 알림(날씨). 키 문자열 전체가 같아야 같은 사건이다.
    /// 위치·날짜 같은 임의 문자가 들어 있어 그대로 쓰지 못하므로 해시로 줄인다.
    /// </summary>
    public static NtfyEventKey ForExact(string kind, string rawKey) =>
        new($"{Slug(kind)}-{Hash(rawKey)}", null, TimeSpan.Zero);

    /// <summary>ntfy 캐시에 있던 sequence_id 가 이 사건과 같은 것인지.</summary>
    public bool Matches(string? sequenceId)
    {
        if (string.IsNullOrEmpty(sequenceId))
            return false;

        if (At is not { } at)
            return string.Equals(sequenceId, Prefix, StringComparison.Ordinal);

        // 접두사(Slug)에는 '_' 가 없으므로 마지막 '_' 가 접두사와 epoch 의 경계다.
        var split = sequenceId.LastIndexOf('_');
        if (split <= 0 || !string.Equals(sequenceId[..split], Prefix, StringComparison.Ordinal))
            return false;

        if (!long.TryParse(sequenceId.AsSpan(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var epoch))
            return false;

        return Math.Abs(epoch - at.ToUnixTimeSeconds()) <= Tolerance.TotalSeconds;
    }

    /// <summary>
    /// ntfy 의 <c>/json?poll=1</c> 응답(줄마다 JSON 하나)에 이 사건의 알림이 이미 있는지.
    ///
    /// Input : 토픽 캐시를 폴링한 NDJSON 본문
    /// Output: 같은 사건의 <c>message</c> 이벤트가 하나라도 있으면 true
    /// 핵심 로직: 본문·제목은 보지 않고 <c>sequence_id</c> 만 본다(PC 이름·상대 시간 문구가 판정에 끼지 않게).
    ///           삭제·읽음 이벤트와 sequence_id 가 없는 예전 버전의 메시지, 깨진 줄은 무시한다.
    /// </summary>
    public bool IsAlreadySent(string? ndjson)
    {
        if (string.IsNullOrWhiteSpace(ndjson))
            return false;

        foreach (var line in ndjson.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    continue;

                if (root.TryGetProperty("event", out var eventEl) &&
                    eventEl.ValueKind == JsonValueKind.String &&
                    eventEl.GetString() != "message")
                    continue;

                if (root.TryGetProperty("sequence_id", out var sequenceEl) &&
                    sequenceEl.ValueKind == JsonValueKind.String &&
                    Matches(sequenceEl.GetString()))
                    return true;
            }
            catch (JsonException)
            {
                // 깨진 줄은 건너뛴다 — 판정 불가는 "중복 아님" 이어야 알림을 잃지 않는다.
            }
        }

        return false;
    }

    /// <summary>ASCII 소문자·숫자·'-' 만 남긴다. 표시용 이름이 언어·공백에 따라 달라져도 키가 흔들리지 않게 한다.</summary>
    internal static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            var lower = char.ToLowerInvariant(ch);
            var isAllowed = lower is >= 'a' and <= 'z' or >= '0' and <= '9';
            if (isAllowed)
                builder.Append(lower);
            else if (builder.Length > 0 && builder[^1] != '-')
                builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "x" : slug;
    }

    /// <summary>
    /// 조각을 이어 붙인 접두사가 길이 한도를 넘으면 잘라 쓰되, 앞부분이 같은 서로 다른 접두사가
    /// 한 키로 합쳐지지 않도록 전체 접두사의 해시를 붙인다. 조각별로 자르면 합쳤을 때 다시 넘을 수 있어 마지막에 한 번만 맞춘다.
    /// </summary>
    private static string Fit(string prefix)
    {
        if (prefix.Length <= MaxPrefixLength)
            return prefix;

        var head = prefix[..(MaxPrefixLength - HashSuffixLength - 1)].TrimEnd('-');
        return $"{head}-{Hash(prefix)}";
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, HashSuffixLength / 2).ToLowerInvariant();
    }
}
