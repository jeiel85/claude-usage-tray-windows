using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 휴대폰 연동 한 덩어리(사용량 피드 + 기기 저장소 + 페어링 + 서버). MainViewModel 은 이것 하나만 알면 된다.
/// 기기 저장소는 처음 켤 때 만든다 — 기능을 쓰지 않는 PC 에 <c>mobile\state.json</c> 이 생기지 않게.
/// </summary>
public sealed class MobileLinkService : IDisposable, IAppPushSender
{
    private static readonly System.Net.Http.HttpClient PushHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly object _gate = new();
    private readonly Func<MobileDeviceStore> _storeFactory;
    private MobileDeviceStore? _store;
    private PairingService? _pairing;
    private MobileApiServer? _server;
    private AppPushSender? _push;
    private bool _enabled;

    public MobileUsageFeed Feed { get; } = new();

    /// <summary>서버 상태나 기기 목록이 바뀌면 알린다(설정 창·연결 창 갱신용, 스레드풀에서 올 수 있다).</summary>
    public event Action? Changed;

    public MobileLinkService(Func<MobileDeviceStore>? storeFactory = null) =>
        _storeFactory = storeFactory ?? (() => new MobileDeviceStore());

    public MobileApiServer? Server
    {
        get
        {
            lock (_gate) return _server;
        }
    }

    public MobileDeviceStore? Devices
    {
        get
        {
            lock (_gate) return _store;
        }
    }

    /// <summary>
    /// Input : 저장된 설정
    /// Output: 없음
    /// 핵심 로직: 꺼져 있으면 서버만 멈추고 기기 목록은 지우지 않는다 — 잠깐 껐다 켜도 휴대폰을 다시 페어링하지 않게.
    ///           LAN·Tailscale 둘 다 꺼져 있으면 열 경로가 없으므로 꺼진 것으로 본다.
    /// </summary>
    public void Apply(NotificationSettings settings)
    {
        var wanted = settings.MobileEnabled && (settings.MobileLan || settings.MobileTailscale || settings.MobileBindLoopback);
        lock (_gate) _enabled = wanted;
        if (!wanted)
        {
            Server?.Stop();
            Changed?.Invoke();
            return;
        }

        var server = EnsureServer();
        server.Start(new MobileServerOptions(
            NormalizePort(settings.MobilePort),
            settings.MobileLan,
            settings.MobileTailscale,
            settings.MobileBindLoopback));
        Changed?.Invoke();
    }

    /// <summary>표시 중인 페어링 코드를 버린다(연결 창을 닫을 때).</summary>
    public void CancelPairing()
    {
        PairingService? pairing;
        lock (_gate) pairing = _pairing;
        pairing?.Cancel();
    }

    public static int NormalizePort(int port) => port is >= 1024 and <= 65535 ? port : MobileProtocol.DefaultPort;

    private MobileApiServer EnsureServer()
    {
        lock (_gate)
        {
            if (_server is not null) return _server;
            _store = _storeFactory();
            _pairing = new PairingService(_store);
            _server = new MobileApiServer(Feed, _store, _pairing);
            _push = new AppPushSender(_store, PushHttp, Environment.MachineName);
            _store.Changed += () => Changed?.Invoke();
            _server.StatusChanged += () => Changed?.Invoke();
            return _server;
        }
    }

    /// <summary>
    /// provider 한 개의 최신 사용량을 피드에 넣는다. 서버가 꺼져 있어도 넣어 둔다 — 켜는 순간 휴대폰이 곧바로
    /// 마지막 값을 받게(다음 갱신 주기까지 빈 화면을 보지 않게).
    /// </summary>
    public void Publish(
        string provider,
        string? accountKey,
        string accountLabel,
        UsageSyncQuotaSnapshot? quota,
        UsageSyncLocalTotals? today,
        string errorKind)
    {
        Feed.Publish(new MobileFeedEntry(
            provider,
            accountKey,
            accountLabel ?? "",
            DateTimeOffset.UtcNow,
            errorKind ?? "",
            quota,
            today is { HasData: true }
                ? new MobileTodayTotals(today.InputTokens, today.OutputTokens, today.CacheReadTokens,
                    today.CacheWriteTokens, today.SessionCount, today.RequestCount)
                : null));
    }

    /// <summary>
    /// 앱 푸시를 받을 휴대폰이 있는가. 휴대폰 연동을 끈 PC 는 구독이 남아 있어도 보내지 않는다 —
    /// 사용자가 끈 기능이 뒤에서 계속 휴대폰으로 데이터를 내보내지 않게.
    /// </summary>
    public bool HasTargets
    {
        get
        {
            lock (_gate) return _enabled && _push?.HasTargets == true;
        }
    }

    public async Task<AppPushResult> SendAsync(
        string category, string title, string? body, int ntfyPriority, string? eventKey, TimeSpan eventTolerance,
        string? clickUrl, CancellationToken ct = default)
    {
        AppPushSender? push;
        lock (_gate) push = _enabled ? _push : null;
        if (push is null) return AppPushResult.NoTargets;
        return await push.SendAsync(push.Build(category, title, body, ntfyPriority, eventKey, eventTolerance, clickUrl), ct);
    }

    public void Dispose()
    {
        Server?.Dispose();
    }
}
