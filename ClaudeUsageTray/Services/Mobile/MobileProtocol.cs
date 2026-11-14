using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 휴대폰 앱(<c>mobile/</c>)과 이 PC 사이의 통신 계약(<c>docs/MOBILE_PROTOCOL.md</c>, #220).
/// 구조는 Homebase(jeiel85/homebase)의 Mobile Protocol v1 을 따른다 — 같은 사람이 두 앱을 함께 쓰므로
/// 페어링·경로·인증 방식이 같아야 사용자가 한 번 익힌 흐름을 그대로 쓸 수 있다.
/// 형식은 <c>docs/mobile-protocol/*.json</c> 픽스처로 고정하고 .NET·Kotlin 테스트가 같은 파일을 검증한다.
/// </summary>
public static class MobileProtocol
{
    /// <summary>메시지 모양 버전. 필드 의미가 바뀔 때만 올린다(추가는 양쪽이 모르는 필드를 무시하므로 그대로).</summary>
    public const int Schema = 1;

    /// <summary><c>/api/v1/info</c> 가 알리는 API 표면 버전.</summary>
    public const int Protocol = 1;

    /// <summary>
    /// 기본 포트. Homebase 가 47820 을 쓰므로 한 PC 에 둘 다 설치해도 겹치지 않게 47830 을 쓴다.
    /// </summary>
    public const int DefaultPort = 47830;

    /// <summary>QR 오퍼를 이 앱 것으로 알아보는 표시 필드(<c>"cu": 1</c>). Homebase QR 의 <c>hb</c> 와 같은 역할.</summary>
    public const int OfferMarker = 1;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>인증 없이 답하는 신원 확인 — 휴대폰 PC 목록의 "온라인"·버전 표시에 쓴다.</summary>
public sealed record PcInfo(int Schema, int Protocol, string PcId, string PcName, string AppVersion, string Os);

/// <summary><see cref="MobileEndpoint.Transport"/> 값.</summary>
public static class MobileTransport
{
    /// <summary>사설 LAN 주소. 자체 서명 인증서 TLS, 앱은 QR 의 SHA-256 으로 핀한다.</summary>
    public const string Lan = "lan";

    /// <summary>Tailscale 주소(100.64.0.0/10) 또는 MagicDNS 이름. WireGuard 가 암호화하므로 평문 HTTP.</summary>
    public const string Tailscale = "tailscale";

    /// <summary>127.0.0.1 — 개발·에뮬레이터(adb reverse) 전용.</summary>
    public const string Loopback = "loopback";
}

/// <summary>이 PC 로 가는 길 하나. LAN 은 <c>CertSha256</c>(DER 의 SHA-256, 소문자 hex)이 있어야 한다.</summary>
public sealed record MobileEndpoint(string Transport, string Host, int Port, bool Tls, string? CertSha256 = null);

/// <summary>페어링 QR 에 담기는 내용. <c>Cu</c> 는 이 앱 QR 표시.</summary>
public sealed record PairingOffer(
    int Cu,
    string PcId,
    string PcName,
    string Code,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<MobileEndpoint> Endpoints);

public sealed record EndpointsResponse(int Schema, IReadOnlyList<MobileEndpoint> Endpoints);

public sealed record PairRequest(string Code, string DeviceName, string? DeviceModel);

public sealed record PairResponse(int Schema, string DeviceId, string Token, string PcId, string PcName);

public sealed record ErrorResponse(int Schema, string Error, string Message);

/// <summary><see cref="MobileProviderUsage.AccountScope"/> 값.</summary>
public static class MobileAccountScope
{
    /// <summary>계정을 알아냈다 — 다른 PC 의 같은 해시와 한 계정으로 합쳐도 된다.</summary>
    public const string Account = "account";

    /// <summary>계정을 모른다 — 해시가 이 PC 를 가리키므로 휴대폰은 PC 별로 따로 보인다.</summary>
    public const string Device = "device";
}

/// <summary>오늘 이 PC 에서 쓴 로컬 토큰 합계(기기 단위 값 — 계정 카드에서는 PC 별로 더한다).</summary>
public sealed record MobileTodayTotals(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int SessionCount,
    int RequestCount);

/// <summary>
/// provider 한 개의 사용량. <c>Quota</c> 는 다중 PC 동기화(<see cref="UsageSyncQuotaSnapshot"/>)와 같은 모양을
/// 그대로 싣는다 — 이미 "계정 단위 할당량" 으로 다듬어진 계약이라 휴대폰에서도 같은 의미로 읽힌다.
/// </summary>
public sealed record MobileProviderUsage(
    string Provider,
    string AccountHash,
    string AccountScope,
    string AccountLabel,
    DateTimeOffset ObservedAt,
    string ErrorKind,
    UsageSyncQuotaSnapshot? Quota,
    MobileTodayTotals? Today);

/// <summary><c>GET /api/v1/usage</c> 응답이자 <c>WS /api/v1/live</c> 의 텍스트 프레임 하나.</summary>
public sealed record UsageSnapshotMessage(
    int Schema,
    DateTimeOffset Ts,
    string PcId,
    string PcName,
    string AppVersion,
    IReadOnlyList<MobileProviderUsage> Providers);

/// <summary>휴대폰이 "같은 계정" 을 알아보는 키.</summary>
public static class MobileAccountHash
{
    private const string Domain = "cut-mobile-v1";

    /// <summary>
    /// Input : provider, 계정 키(Claude orgUuid|accountUuid, Codex account_id|user_id — 모르면 null), 이 PC 의 pcId
    /// Output: (해시, 범위)
    /// 핵심 로직: 원시 계정 ID 는 휴대폰에도 보내지 않는다 — 합치는 데는 "같은가" 만 알면 되므로 SHA-256 만 보낸다.
    ///           계정을 모르면 provider 만으로 해시하지 않고 pcId 를 섞는다. 그렇게 하지 않으면 서로 다른 계정을 쓰는
    ///           PC 들이 "알 수 없음" 이라는 이유로 한 카드에 합쳐져, 남의 할당량을 내 계정 값처럼 보이게 된다.
    /// </summary>
    public static (string Hash, string Scope) For(string provider, string? accountKey, string pcId)
    {
        var normalizedProvider = (provider ?? "").Trim().ToLowerInvariant();
        var key = accountKey?.Trim();
        var (material, scope) = string.IsNullOrEmpty(key)
            ? ($"{Domain}|device|{normalizedProvider}|{pcId}", MobileAccountScope.Device)
            : ($"{Domain}|account|{normalizedProvider}|{key}", MobileAccountScope.Account);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return (hash, scope);
    }
}
