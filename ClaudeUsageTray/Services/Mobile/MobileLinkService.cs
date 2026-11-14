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

    /// <summary>PC 직접 동기화(#223)의 공유 표 — 이 PC 가 아는 모든 PC 의 최신 스냅샷.</summary>
    public PeerSyncHub PeerSync { get; } = new();

    private readonly Lazy<SyncPeerStore> _peerStore;
    private PeerSyncClient? _peerClient;

    /// <summary>서버 상태나 기기 목록이 바뀌면 알린다(설정 창·연결 창 갱신용, 스레드풀에서 올 수 있다).</summary>
    public event Action? Changed;

    public MobileLinkService(Func<MobileDeviceStore>? storeFactory = null, Func<SyncPeerStore>? peerStoreFactory = null)
    {
        _storeFactory = storeFactory ?? (() => new MobileDeviceStore());
        _peerStore = new Lazy<SyncPeerStore>(peerStoreFactory ?? (() => new SyncPeerStore()));
    }

    /// <summary>
    /// PC 직접 동기화가 쓰이고 있는가: 이 PC 가 연결해 둔 PC 가 있거나, 다른 PC 가 이 PC 에 연결해 두었거나,
    /// 이미 다른 PC 의 값을 받았다. 아니면 MainViewModel 은 예전처럼 동기화를 건너뛴다.
    /// </summary>
    public bool IsPeerSyncActive =>
        _peerStore.Value.All().Count > 0
        || (Devices?.List().Any(d => d.Kind == MobileDeviceKind.Pc) ?? false)
        || PeerSync.RemoteDeviceCount > 0;

    /// <summary>동기화 코드로 연결한 PC 목록과 연결 상태.</summary>
    public PeerSyncClient Peers
    {
        get
        {
            lock (_gate)
            {
                if (_peerClient is not null) return _peerClient;
                _peerClient = new PeerSyncClient(PeerSync, _peerStore.Value, () => EnsureStore().PcId);
                _peerClient.Changed += () => Changed?.Invoke();
                return _peerClient;
            }
        }
    }

    /// <summary>
    /// 다른 PC 에 붙일 동기화 코드. 휴대폰 QR 과 같은 일회용 오퍼라, 서버가 꺼져 있거나 열린 경로가 없으면 null.
    /// </summary>
    public string? CreateSyncCode() => Server?.CreateOffer() is { } offer ? PairingLink.Build(offer) : null;

    private MobileDeviceStore EnsureStore()
    {
        lock (_gate)
        {
            _store ??= _storeFactory();
            return _store;
        }
    }

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
        // 연결해 둔 PC 와의 동기화는 이 PC 의 포트를 열지 않아도 된다(이 PC 가 여는 쪽) — 기능 토글과 무관하게 이어 간다.
        Peers.Reconcile();
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
            _store ??= _storeFactory();
            _pairing = new PairingService(_store);
            _server = new MobileApiServer(Feed, _store, _pairing, sync: PeerSync);
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
                ? new MobileTodayTotals(
                    DateOnly.FromDateTime(DateTime.Now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    today.InputTokens, today.OutputTokens, today.CacheReadTokens,
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
        _peerClient?.Dispose();
        Server?.Dispose();
    }
}
