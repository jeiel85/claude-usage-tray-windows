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
        ? $"{Prefix}_{PublishedEpoch(at).ToString(CultureInfo.InvariantCulture)}"
        : Prefix;

    // 발행하는 시각 조각은 분 단위로 내린다. 몇 초 차이로 같은 사건을 감지한 두 PC 가 서로 다른 sequence_id 를 발행하면,
    // 같은 sequence_id 끼리만 한 알림으로 합치는 Android·웹의 기능을 쓸 수 없다(동시 감지 경합에서 이 기능에 기대고 있다).
    // 1분 안이면 같은 ID 가 나온다. 비교(Matches)는 이 내림 폭만큼 넉넉하게 봐서, 원래 맞던 것은 그대로 맞는다.
    private const long PublishedEpochStepSeconds = 60;

    private static long PublishedEpoch(DateTimeOffset at) =>
        Math.Max(0, at.ToUnixTimeSeconds()) / PublishedEpochStepSeconds * PublishedEpochStepSeconds;

    // accountId 를 받는 이유: 같은 토픽을 쓰는 PC 들이 서로 다른 계정으로 로그인해 있으면(회사·개인 등) 공급자·창·임계값이
    // 같은 알림이 서로 다른 계정의 사건인데도 같은 키가 되어, 뒤에 감지한 쪽의 정당한 알림이 삼켜진다.
    // 중복을 못 잡는 것은 예전과 같은 동작이지만 알림을 놓치는 것은 회귀이므로, 계정이 다르면 키도 달라야 한다.
    // 원문은 공개 토픽 캐시에 남으므로 해시만 싣고, 같은 계정이면 어느 PC 에서든 같은 값이 나온다.
    //
    // 계정을 모르면(null·공백: API 키 모드처럼 식별자가 없는 로그인, 일시적 읽기 실패) 기기 구분자를 대신 넣는다.
    // 모르는 쪽끼리 "모름" 이라는 이유로 같은 키가 되면, 서로 다른 계정일 수 있는 두 PC 의 알림이 서로를 삼킨다.
    // 기기 구분자를 쓰면 다른 PC 와는 절대 일치하지 않아 중복 쪽으로만 어긋나고, 같은 PC 의 반복 알림은 여전히 걸러진다.
    // deviceId 는 테스트에서 "서로 다른 PC" 를 흉내 내기 위한 것이고 앱은 넘기지 않는다(기본값 = 이 PC).

    /// <summary>
    /// 임계값 도달 알림. 창의 리셋 시각이 서버 값이면 그것으로 "같은 창" 을 가르고,
    /// 없거나 추정치면(PC 마다 달라짐) 감지 시각으로 대신한다.
    /// </summary>
    public static NtfyEventKey ForUsage(
        string agent, string windowId, int thresholdPercent, DateTimeOffset? windowResetAt, DateTimeOffset now,
        string? accountId = null, string? deviceId = null)
    {
        // 키는 PC 간에 같아야 하므로 숫자 서식도 문화권을 따르지 않게 한다.
        var prefix = string.Create(CultureInfo.InvariantCulture,
            $"usage-{Slug(agent)}-{Slug(windowId)}-{thresholdPercent}{ScopeSegment(accountId, deviceId)}");
        return windowResetAt is { } reset
            ? new NtfyEventKey(prefix, reset, WindowResetTolerance)
            : new NtfyEventKey(prefix, now, DetectionTolerance);
    }

    /// <summary>초기화·레이트 리밋처럼 "지금 일어난" 사건. 같은 공급자의 같은 종류는 한 번 일어나면 한동안 다시 일어나지 않는다.</summary>
    public static NtfyEventKey ForInstant(
        string kind, string agent, DateTimeOffset now, string? accountId = null, string? deviceId = null) =>
        new($"{Slug(kind)}-{Slug(agent)}{ScopeSegment(accountId, deviceId)}", now, DetectionTolerance);

    /// <summary>
    /// 할당량 초기화 알림. <paramref name="endedWindowResetAt"/>(초기화로 끝난 창의 서버 리셋 시각)을 알면 그것으로 사건을 가른다.
    /// 감지한 시각으로 가르면, 초기화를 자고 넘겨 30분 넘게 늦게 깬 PC 가 다른 PC 가 이미 보낸 같은 초기화를 알아보지 못하고 다시 보낸다.
    /// 모르면(추정 리셋·첫 관측 등) 감지 시각으로 대신한다.
    /// </summary>
    public static NtfyEventKey ForReset(
        string agent, DateTimeOffset? endedWindowResetAt, DateTimeOffset now, string? accountId = null, string? deviceId = null)
    {
        var prefix = $"reset-{Slug(agent)}{ScopeSegment(accountId, deviceId)}";
        return endedWindowResetAt is { } ended
            ? new NtfyEventKey(prefix, ended, WindowResetTolerance)
            : new NtfyEventKey(prefix, now, DetectionTolerance);
    }

    /// <summary>조기 소진 알림. 같은 창 안에서 예상이 앞당겨지면(=시각이 크게 달라지면) 새 사건으로 본다.</summary>
    public static NtfyEventKey ForEarlyExhaustion(
        string agent, DateTimeOffset? depletionAt, DateTimeOffset now, string? accountId = null, string? deviceId = null) =>
        new($"early-{Slug(agent)}{ScopeSegment(accountId, deviceId)}", depletionAt ?? now,
            depletionAt is null ? DetectionTolerance : DepletionTolerance);

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

        // 저장된 값은 분 단위로 내린 것이라 실제 시각은 [epoch, epoch + 59] 안 어딘가다.
        // 그 안에서 허용 오차 이내인 시각이 하나라도 있으면 같은 사건으로 본다.
        var seconds = at.ToUnixTimeSeconds();
        var tolerance = (long)Tolerance.TotalSeconds;
        return seconds >= epoch - tolerance && seconds <= epoch + (PublishedEpochStepSeconds - 1) + tolerance;
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

    // 이 실행 중인 앱을 가리키는 값 — 프로세스마다 새로 뽑는 무작위 값이다.
    // 컴퓨터 이름·사용자 이름 같은 눈에 보이는 이름은 이미지 복제·같은 이름 설정 등으로 서로 다른 PC 에서 겹칠 수 있고,
    // 겹치면 계정을 모르는 두 PC 가 다시 같은 키가 되어 서로의 알림을 삼킨다(Codex 리뷰봇 3차 지적).
    // 영속 ID 는 필요 없다: 알림은 실행 중인 프로세스가 직접 본 전환에서만 나가고(시작 직후의 첫 관측은 기준선만 만든다),
    // 그래서 재시작을 넘어 "같은 PC 의 이전 알림" 과 맞출 일이 없다. 같은 실행 안의 반복만 걸러지면 충분하다.
    private static readonly string DefaultDeviceId = Guid.NewGuid().ToString("N");

    // 계정을 알면 계정 조각("-a" + 해시 8자리), 모르면 기기 조각("-d" + 해시 8자리). 접두사가 서로 달라 둘이 섞여 일치할 수 없다.
    // 식별자 원문은 공개 토픽 캐시에 남기지 않으려고 해시만 싣는다.
    private static string ScopeSegment(string? accountId, string? deviceId) =>
        string.IsNullOrWhiteSpace(accountId)
            ? $"-d{Hash(string.IsNullOrWhiteSpace(deviceId) ? DefaultDeviceId : deviceId.Trim())[..8]}"
            : $"-a{Hash(accountId.Trim())[..8]}";

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, HashSuffixLength / 2).ToLowerInvariant();
    }
}
