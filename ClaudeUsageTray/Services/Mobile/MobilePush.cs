using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// <c>POST /api/v1/push</c> 본문: 휴대폰의 UnifiedPush 엔드포인트와 RFC 8291 구독 키(둘 다 패딩 없는 base64url).
/// <c>MutedCategories</c> 는 이 휴대폰이 받지 않을 알림 범주(없으면 전부 받음) — Homebase DECISIONS M-25 와 같은 규칙.
/// </summary>
public sealed record PushSubscriptionRequest(
    int Schema, string Endpoint, string P256dh, string Auth, IReadOnlyList<string>? MutedCategories = null);

public sealed record PushSubscriptionResponse(int Schema, string DeviceId, bool Registered);

/// <summary>저장된 구독(<c>state.json</c> 의 그 기기 항목 안).</summary>
public sealed record PushSubscription(
    string Endpoint, string P256dh, string Auth, DateTimeOffset UpdatedAt, IReadOnlyList<string>? MutedCategories = null)
{
    /// <summary>이 휴대폰이 <paramref name="category"/> 를 꺼 두었는가. 테스트 알림은 끌 수 없다(경로 확인용).</summary>
    public bool Mutes(string category) =>
        category != PushCategories.Test && MutedCategories is { Count: > 0 } muted && muted.Contains(category, StringComparer.Ordinal);
}

/// <summary>푸시를 받을 기기 하나.</summary>
public sealed record PushTarget(string DeviceId, string DeviceName, PushSubscription Subscription);

/// <summary>이 앱이 보내는 알림 범주. 휴대폰 앱의 "받을 알림" 그룹과 같은 이름을 쓴다.</summary>
public static class PushCategories
{
    public const string Usage = "usage";
    public const string RateLimit = "rate_limit";
    public const string Reset = "reset";
    public const string Exhaustion = "exhaustion";
    public const string Weather = "weather";
    public const string Test = "test";

    public const int MaxMuted = 32;
    public const int MaxLength = 64;

    /// <summary>정렬·중복 제거, 비었으면 null — 같은 선택이면 같은 값으로 비교되게.</summary>
    public static IReadOnlyList<string>? Normalize(IReadOnlyList<string>? muted) =>
        muted is null || muted.Count == 0
            ? null
            : muted.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static bool SameMuted(IReadOnlyList<string>? a, IReadOnlyList<string>? b) =>
        (Normalize(a) ?? []).SequenceEqual(Normalize(b) ?? [], StringComparer.Ordinal);

    /// <summary>범주 이름: <c>a-z 0-9 _ -</c> 1~64자. 모르는 범주도 받아 둔다(앱이 PC 보다 새로울 수 있다).</summary>
    public static bool IsValid(string? category) =>
        !string.IsNullOrEmpty(category) && category.Length <= MaxLength
        && category.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
}

public static class PushPriority
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";

    /// <summary>기존 ntfy 우선순위(1~5)를 앱 푸시 우선순위로.</summary>
    public static string FromNtfy(int priority) => priority switch
    {
        >= 5 => Critical,
        4 => Warning,
        _ => Info,
    };
}

/// <summary>
/// 복호화된 푸시 내용(UTF-8 JSON, 픽스처 <c>push-message.v1.json</c>).
/// <c>EventKey</c>·<c>EventToleranceSec</c> 는 기존 ntfy 의 <c>sequence_id</c> 중복 방지(#175)를 앱으로 옮긴 것이다 —
/// 같은 계정을 보는 여러 PC 가 같은 사건을 각자 보내도 휴대폰은 하나만 띄운다.
/// </summary>
public sealed record PushMessage(
    int Schema,
    string Id,
    string PcId,
    string PcName,
    string Category,
    string Priority,
    string Title,
    string? Body,
    string? EventKey,
    int EventToleranceSec,
    string? ClickUrl,
    DateTimeOffset Ts);

public static class PushPayload
{
    public const int MaxEndpointBytes = 1000;

    private const string Ellipsis = "…";

    /// <summary>
    /// Input : 알림 1건
    /// Output: 암호화 한도(<see cref="WebPushEncryption.MaxPlaintextBytes"/>) 안의 UTF-8 JSON
    /// 핵심 로직: 넘치면 본문을 먼저, 그래도 넘치면 제목을 줄인다(끝에 …). 알림을 통째로 버리는 것보다 잘린 알림이 낫다.
    /// </summary>
    public static byte[] Serialize(PushMessage message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, MobileProtocol.Json);
        if (bytes.Length <= WebPushEncryption.MaxPlaintextBytes) return bytes;

        var over = bytes.Length - WebPushEncryption.MaxPlaintextBytes;
        var body = message.Body ?? "";
        while (body.Length > 0)
        {
            body = Truncate(body, Math.Max(0, body.Length - Math.Max(1, over / 3)));
            bytes = JsonSerializer.SerializeToUtf8Bytes(message with { Body = body + Ellipsis }, MobileProtocol.Json);
            if (bytes.Length <= WebPushEncryption.MaxPlaintextBytes) return bytes;
            over = bytes.Length - WebPushEncryption.MaxPlaintextBytes;
        }

        var title = message.Title;
        while (title.Length > 0)
        {
            title = Truncate(title, Math.Max(0, title.Length - Math.Max(1, over / 3)));
            bytes = JsonSerializer.SerializeToUtf8Bytes(message with { Body = null, Title = title + Ellipsis }, MobileProtocol.Json);
            if (bytes.Length <= WebPushEncryption.MaxPlaintextBytes) return bytes;
            over = bytes.Length - WebPushEncryption.MaxPlaintextBytes;
        }
        throw new ArgumentException("Push message does not fit even without title and body.", nameof(message));
    }

    /// <summary>휴대폰이 보낸 구독을 검사한다. 문제가 있으면 오류 문구, 없으면 null.</summary>
    public static string? Validate(PushSubscriptionRequest? request)
    {
        if (request is null) return "Invalid subscription.";
        if (request.Schema != MobileProtocol.Schema) return $"Unsupported schema {request.Schema}.";
        if (string.IsNullOrWhiteSpace(request.Endpoint) || Encoding.UTF8.GetByteCount(request.Endpoint) > MaxEndpointBytes)
            return "Endpoint is missing or too long.";
        // https 만 받는다 — PC 가 이 주소로 POST 하므로, 평문 http 나 사용자 정보가 든 주소로 알림을 흘리지 않게.
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(uri.UserInfo))
            return "Endpoint must be an https URL.";
        try
        {
            WebPushEncryption.ValidateSubscriptionKeys(Base64Url.Decode(request.P256dh), Base64Url.Decode(request.Auth));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return "Subscription keys are invalid.";
        }
        if (request.MutedCategories is { } muted
            && (muted.Count > PushCategories.MaxMuted || !muted.All(PushCategories.IsValid)))
            return "Muted categories are invalid.";
        return null;
    }

    private static string Truncate(string s, int length)
    {
        if (length <= 0) return "";
        if (length < s.Length && char.IsHighSurrogate(s[length - 1])) length--;
        return s[..length];
    }
}

/// <summary>패딩 없는 base64url(RFC 4648 §5) — Web Push 키 인코딩.</summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string? text)
    {
        if (string.IsNullOrEmpty(text)) throw new FormatException("Empty base64url value.");
        var s = text.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", 0 => s, _ => throw new FormatException("Bad base64url length.") };
        return Convert.FromBase64String(s);
    }
}
