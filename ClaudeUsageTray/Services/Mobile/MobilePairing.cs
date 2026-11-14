using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaudeUsageTray.Services.Mobile;

public enum PairOutcome { Paired, InvalidCode, Expired, Locked }

public sealed record PairResult(PairOutcome Outcome, string? DeviceId = null, string? Token = null);

/// <summary>
/// QR 일회용 페어링 코드(Homebase DECISIONS M-2 와 같은 규칙).
/// 활성 코드는 하나뿐(새 QR 이 이전 것을 무효화), 5분 수명, 1회용, 5분에 10번 틀리면 잠근다.
/// </summary>
public sealed class PairingService
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    public const int MaxFailures = 10;

    private readonly MobileDeviceStore _store;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _failures = new();
    private string? _code;
    private DateTimeOffset _expiresAt;

    public PairingService(MobileDeviceStore store, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Input : 없음
    /// Output: 새 코드(128bit base64url)와 만료 시각
    /// 핵심 로직: 128bit 라 추측은 사실상 불가능하지만, 같은 LAN 의 누구나 시도할 수 있으므로 잠금(<see cref="TryPair"/>)을 함께 둔다.
    /// </summary>
    public (string Code, DateTimeOffset ExpiresAt) Begin()
    {
        lock (_gate)
        {
            _code = Base64Url(RandomNumberGenerator.GetBytes(16));
            _expiresAt = _time.GetUtcNow() + CodeLifetime;
            return (_code, _expiresAt);
        }
    }

    /// <summary>창을 닫거나 기능을 끌 때 남은 코드를 버린다 — 화면에서 사라진 QR 이 5분간 살아 있을 이유가 없다.</summary>
    public void Cancel()
    {
        lock (_gate) _code = null;
    }

    /// <summary>
    /// Input : 휴대폰이 보낸 코드·기기 이름·모델
    /// Output: 성공이면 기기 ID 와 256bit 토큰(평문은 이때 한 번만 나간다)
    /// 핵심 로직: 실패를 먼저 세고(잠금 확인) 그 뒤 고정시간 비교 — 비교 시간으로 코드를 한 글자씩 맞혀 가지 못하게.
    ///           만료는 맞는 코드일 때만 알려 준다(틀린 코드에 "만료" 를 답하면 코드가 있었다는 사실이 새어 나간다).
    /// </summary>
    public PairResult TryPair(string? code, string? deviceName, string? deviceModel, string? kind = null)
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            while (_failures.Count > 0 && now - _failures.Peek() > FailureWindow) _failures.Dequeue();
            if (_failures.Count >= MaxFailures) return new PairResult(PairOutcome.Locked);

            if (_code is null || string.IsNullOrEmpty(code) ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(_code), Encoding.ASCII.GetBytes(code)))
            {
                _failures.Enqueue(now);
                return new PairResult(PairOutcome.InvalidCode);
            }

            if (now > _expiresAt)
            {
                _code = null;
                return new PairResult(PairOutcome.Expired);
            }

            _code = null;
            var token = Base64Url(RandomNumberGenerator.GetBytes(32));
            var device = _store.Add(CleanName(deviceName), CleanModel(deviceModel), HashToken(token), MobileDeviceKind.Normalize(kind));
            return new PairResult(PairOutcome.Paired, device.DeviceId, token);
        }
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CleanName(string? name)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) return "Android";
        return n.Length > 64 ? n[..64] : n;
    }

    private static string? CleanModel(string? model)
    {
        var m = model?.Trim();
        if (string.IsNullOrEmpty(m)) return null;
        return m.Length > 64 ? m[..64] : m;
    }
}

/// <summary>
/// 페어링 QR 을 https 링크로 만든다(Homebase DECISIONS M-20):
/// <c>https://jeiel85.github.io/claude-usage-tray-windows/pair/#&lt;base64url(오퍼 JSON)&gt;</c>.
/// 앱이 있으면 링크가 앱의 페어링 화면을 열고, 없으면 휴대폰 기본 카메라가 설치 안내 페이지(<c>docs/pair/</c>)를 연다.
/// 오퍼는 fragment(#)에 있어 브라우저가 서버로 보내지 않으므로 일회용 코드가 접근 로그에 남지 않는다.
/// </summary>
public static class PairingLink
{
    public const string Prefix = "https://jeiel85.github.io/claude-usage-tray-windows/pair/#";

    public static string Build(PairingOffer offer) =>
        Prefix + PairingService.Base64Url(JsonSerializer.SerializeToUtf8Bytes(offer, MobileProtocol.Json));

    /// <summary>링크 안의 오퍼 JSON. 이 앱 링크가 아니거나 base64 가 깨졌으면 null.</summary>
    public static string? DecodeJson(string link)
    {
        if (!link.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        var b64 = link[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch (FormatException) { return null; }
    }
}
