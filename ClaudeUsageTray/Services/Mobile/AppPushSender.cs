using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>앱 푸시 1건의 결과. 알림 경로 선택(앱 푸시 → 기존 ntfy 토픽)에 쓴다.</summary>
public enum AppPushResult
{
    /// <summary>푸시를 등록한 휴대폰이 없다 — 기존 토픽으로 보낸다.</summary>
    NoTargets,

    /// <summary>한 대 이상이 받았다.</summary>
    Delivered,

    /// <summary>모든 휴대폰이 이 범주를 껐다 — 사용자의 선택이므로 토픽으로도 보내지 않는다.</summary>
    AllMuted,

    /// <summary>모두 실패했다 — 토픽이 있으면 그쪽으로 대신 보낸다.</summary>
    Failed,
}

/// <summary>NotificationService 가 아는 앱 푸시 경로(휴대폰 연동이 꺼져 있으면 대상이 없다).</summary>
public interface IAppPushSender
{
    bool HasTargets { get; }

    Task<AppPushResult> SendAsync(
        string category, string title, string? body, int ntfyPriority, string? eventKey, TimeSpan eventTolerance,
        string? clickUrl, CancellationToken ct = default);
}

/// <summary>
/// 푸시를 등록한 모든 휴대폰에 알림을 보낸다(Homebase <c>UnifiedPushOutboundChannel</c> 이식, #222).
/// 본문은 RFC 8291 <c>aes128gcm</c> 이라 중계 서버(ntfy.sh 등)는 암호문만 본다 — 기존 평문 토픽과 달리
/// 사용량·계정 이름이 제3자 서버에 남지 않는다.
/// <list type="bullet">
/// <item>429: 그 엔드포인트를 <c>Retry-After</c>(기본 60초, 최대 1시간) 동안 쉬게 한다.</item>
/// <item>404·410: 엔드포인트가 사라졌으므로 구독을 지운다(앱이 다음 연결 때 다시 등록한다).</item>
/// </list>
/// </summary>
public sealed class AppPushSender
{
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    /// <summary>사용량 알림은 하루 지나면 의미가 없다 — 그 사이 꺼져 있던 휴대폰은 옛 경고를 받지 않는다.</summary>
    private const int TtlSeconds = 24 * 3600;

    private readonly MobileDeviceStore _devices;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string _pcName;
    private readonly Dictionary<string, DateTimeOffset> _blockedUntil = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public AppPushSender(MobileDeviceStore devices, HttpClient http, string pcName, TimeProvider? time = null)
    {
        _devices = devices;
        _http = http;
        _pcName = pcName;
        _time = time ?? TimeProvider.System;
    }

    public bool HasTargets => _devices.PushTargets().Count > 0;

    /// <summary>
    /// Input : 범주·제목·본문·우선순위(ntfy 1~5)·사건 키
    /// Output: <see cref="AppPushResult"/>
    /// 핵심 로직: 범주를 끈 휴대폰은 건너뛴다. 모두가 끈 범주는 "성공(AllMuted)" — 끈 것은 실패가 아니라 선택이므로
    ///           기존 토픽으로 넘기지 않는다. 한 대라도 받으면 Delivered.
    /// </summary>
    public async Task<AppPushResult> SendAsync(PushMessage message, CancellationToken ct = default)
    {
        var targets = _devices.PushTargets();
        if (targets.Count == 0) return AppPushResult.NoTargets;
        var wanted = targets.Where(t => !t.Subscription.Mutes(message.Category)).ToList();
        if (wanted.Count == 0) return AppPushResult.AllMuted;

        var plaintext = PushPayload.Serialize(message);
        var urgency = message.Priority is PushPriority.Critical or PushPriority.Warning ? "high" : "normal";
        var delivered = 0;
        foreach (var target in wanted)
        {
            if (await SendOneAsync(target, plaintext, urgency, ct)) delivered++;
        }
        return delivered > 0 ? AppPushResult.Delivered : AppPushResult.Failed;
    }

    /// <summary>경로 갱신 푸시를 받을 수 있는 기기(구독과 토큰 해시가 둘 다 있는 기기).</summary>
    public IReadOnlyList<string> EndpointTargetIds() =>
        _devices.PushTargets().Where(t => t.TokenSha256 is { Length: 64 }).Select(t => t.DeviceId).ToList();

    /// <summary>
    /// Input : 이 PC 의 현재 경로, 보낼 기기 ID 들
    /// Output: 받은 기기 ID 들
    /// 핵심 로직: 기기마다 그 기기 토큰 해시로 따로 서명한다(<see cref="PushEndpoints.Serialize"/>). 알림 범주가 아니므로
    ///           "받을 알림" 선택과 무관하게 보낸다 — 휴대폰에 아무것도 띄우지 않고 경로만 갱신한다.
    /// </summary>
    public async Task<IReadOnlyList<string>> SendEndpointsAsync(
        IReadOnlyList<MobileEndpoint> endpoints, IReadOnlyCollection<string> deviceIds, CancellationToken ct = default)
    {
        var delivered = new List<string>();
        var ts = _time.GetUtcNow();
        foreach (var target in _devices.PushTargets())
        {
            if (target.TokenSha256 is not { Length: 64 } hash || !deviceIds.Contains(target.DeviceId)) continue;
            byte[] plaintext;
            try
            {
                plaintext = PushEndpoints.Serialize(_devices.PcId, endpoints, ts, hash);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                GC.KeepAlive(ex);
                continue;
            }
            if (await SendOneAsync(target, plaintext, "normal", ct)) delivered.Add(target.DeviceId);
        }
        return delivered;
    }

    public PushMessage Build(string category, string title, string? body, int ntfyPriority, string? eventKey,
        TimeSpan eventTolerance, string? clickUrl) =>
        new(MobileProtocol.Schema,
            Guid.NewGuid().ToString("N"),
            _devices.PcId,
            _pcName,
            category,
            PushPriority.FromNtfy(ntfyPriority),
            title,
            string.IsNullOrEmpty(body) ? null : body,
            eventKey,
            (int)Math.Max(0, eventTolerance.TotalSeconds),
            clickUrl,
            _time.GetUtcNow());

    private async Task<bool> SendOneAsync(PushTarget target, byte[] plaintext, string urgency, CancellationToken ct)
    {
        var endpoint = target.Subscription.Endpoint;
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_blockedUntil.TryGetValue(endpoint, out var until))
            {
                if (now < until) return false;
                _blockedUntil.Remove(endpoint);
            }
        }

        byte[] body;
        try
        {
            body = WebPushEncryption.Encrypt(
                plaintext, Base64Url.Decode(target.Subscription.P256dh), Base64Url.Decode(target.Subscription.Auth));
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return false; // 등록 때 검사한 키라 다시 시도해도 나아지지 않는다.
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", TtlSeconds.ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", urgency);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }

        using (response)
        {
            if (response.IsSuccessStatusCode) return true;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = RetryAfter(response, _time.GetUtcNow());
                lock (_gate) _blockedUntil[endpoint] = _time.GetUtcNow() + wait;
            }
            else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                var current = _devices.PushTargets().FirstOrDefault(t => t.DeviceId == target.DeviceId);
                if (current is not null && current.Subscription.Endpoint == endpoint)
                    _devices.SetPushSubscription(target.DeviceId, null);
            }
            return false;
        }
    }

    internal static TimeSpan RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        TimeSpan? wait = header?.Delta ?? (header?.Date is { } date ? date - now : null);
        if (wait is null || wait <= TimeSpan.Zero) return DefaultRetryAfter;
        return wait > MaxRetryAfter ? MaxRetryAfter : wait.Value;
    }
}
