using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>휴대폰 서버 설정. 설정 파일(<c>settings.json</c>)의 <c>Mobile*</c> 값에서 만든다.</summary>
public sealed record MobileServerOptions(int Port, bool Lan, bool Tailscale, bool BindLoopback = false);

/// <summary>설정 창에 보이는 서버 상태.</summary>
public sealed record MobileServerStatus(bool Running, bool Listening, IReadOnlyList<MobileEndpoint> Endpoints, int Watchers, string? LastError);

/// <summary>
/// 휴대폰용 API(<c>docs/MOBILE_PROTOCOL.md</c>). Homebase <c>MobileApiServer</c> 를 트레이 앱 안에서 돌도록 옮겼다.
/// 와일드카드에 바인딩하지 않고 명시 주소에만 연다. 주소가 바뀌면(DHCP·Wi-Fi 전환·Tailscale 로그인) 30초 안에 다시 바인딩한다.
/// <list type="bullet">
/// <item>LAN: 기본 게이트웨이가 있는 어댑터의 사설 IPv4, 핀 고정 자체 서명 인증서 TLS.</item>
/// <item>Tailscale: 100.64.0.0/10 주소, 평문(WireGuard 가 암호화).</item>
/// <item>루프백: 개발·에뮬레이터 전용(설정 파일에서만 켤 수 있음).</item>
/// </list>
/// 이 서버의 실패는 트레이 본기능(사용량 표시)을 멈추지 않는다 — 모든 예외를 잡아 상태 문구에 남기고 다시 시도한다.
/// </summary>
public sealed class MobileApiServer : IDisposable
{
    private static readonly TimeSpan RebindInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeadTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 변화가 없어도 이 간격마다 스냅샷을 다시 보낸다. 휴대폰은 마지막 수신 시각으로 "PC 응답 없음" 을 판단하고,
    /// 사용량 갱신 주기가 길게(최대 수십 분) 설정되어도 연결이 살아 있음을 알 수 있다.
    /// </summary>
    public static readonly TimeSpan LiveHeartbeat = TimeSpan.FromSeconds(60);

    private const int MaxConnections = 32;

    /// <summary>철회된 기기에 보내는 WebSocket 종료 사유(1008). 앱은 401 과 같게 처리한다.</summary>
    public const string RevokedCloseReason = "revoked";

    private enum PathKind { Tailnet, Lan, Loopback }

    private sealed record Binding(IPAddress Address, PathKind Kind, int PrefixLength)
    {
        public string Key => $"{Kind}:{Address}/{PrefixLength}";
    }

    private readonly MobileUsageFeed _feed;
    private readonly MobileDeviceStore _devices;
    private readonly PairingService _pairing;
    private readonly string _pcName;
    private readonly SemaphoreSlim _connections = new(MaxConnections);
    private readonly object _gate = new();
    private MobileServerOptions _options = new(MobileProtocol.DefaultPort, true, true);
    private CancellationTokenSource? _cts;
    private IReadOnlyList<Binding> _bound = [];
    private string? _magicDns;
    private X509Certificate2? _lanCertificate;
    private string? _lanPin;
    private bool _listening;
    private string? _lastError;

    /// <summary>상태가 바뀌면(바인딩 성공·실패·주소 변경) 알린다. 설정 창의 QR 을 새 경로로 다시 만들 때 쓴다.</summary>
    public event Action? StatusChanged;

    public MobileApiServer(MobileUsageFeed feed, MobileDeviceStore devices, PairingService pairing, string? pcName = null)
    {
        _feed = feed;
        _devices = devices;
        _pairing = pairing;
        _pcName = string.IsNullOrWhiteSpace(pcName) ? Environment.MachineName : pcName.Trim();
    }

    public static string AppVersion =>
        typeof(MobileApiServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0] ?? "0.0.0";

    public bool IsRunning
    {
        get
        {
            lock (_gate) return _cts is not null;
        }
    }

    public MobileServerOptions Options
    {
        get
        {
            lock (_gate) return _options;
        }
    }

    /// <summary>
    /// Input : 서버 설정
    /// Output: 없음(백그라운드 루프 시작)
    /// 핵심 로직: 이미 같은 설정으로 돌고 있으면 아무것도 하지 않는다 — 설정 저장마다 연결 중인 휴대폰을 끊지 않게.
    /// </summary>
    public void Start(MobileServerOptions options)
    {
        lock (_gate)
        {
            if (_cts is not null && _options == options) return;
        }
        Stop();
        lock (_gate)
        {
            _options = options;
            _cts = new CancellationTokenSource();
            _lastError = null;
            var token = _cts.Token;
            _ = Task.Run(() => RunAsync(token));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _listening = false;
            _bound = [];
        }
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
        _pairing.Cancel();
        StatusChanged?.Invoke();
    }

    public MobileServerStatus Status()
    {
        lock (_gate) return new MobileServerStatus(_cts is not null, _listening, EndpointsLocked(), _feed.Watchers, _lastError);
    }

    public IReadOnlyList<MobileEndpoint> Endpoints()
    {
        lock (_gate) return EndpointsLocked();
    }

    /// <summary>새 일회용 페어링 오퍼(QR 내용). 열린 경로가 하나도 없으면 null — 닿지 않는 QR 을 보여 주지 않는다.</summary>
    public PairingOffer? CreateOffer()
    {
        IReadOnlyList<MobileEndpoint> endpoints;
        lock (_gate)
        {
            if (!_listening) return null;
            endpoints = EndpointsLocked();
        }
        if (endpoints.Count == 0) return null;
        var (code, expires) = _pairing.Begin();
        return new PairingOffer(MobileProtocol.OfferMarker, _devices.PcId, _pcName, code, expires, endpoints);
    }

    /// <summary>
    /// 경로를 앱의 기본 선호 순서(LAN → Tailscale 이름 → Tailscale IP → 루프백)로 나열한다.
    /// MagicDNS 이름을 IP 보다 앞에 두는 이유는 <see cref="MobileNetwork.FindMagicDnsName"/> 참고.
    /// </summary>
    private IReadOnlyList<MobileEndpoint> EndpointsLocked()
    {
        var list = new List<MobileEndpoint>();
        if (!_listening) return list;
        var port = _options.Port;
        foreach (var b in _bound.Where(b => b.Kind == PathKind.Lan))
            list.Add(new MobileEndpoint(MobileTransport.Lan, b.Address.ToString(), port, true, _lanPin));
        if (_bound.Any(b => b.Kind == PathKind.Tailnet) && _magicDns is { } dns)
            list.Add(new MobileEndpoint(MobileTransport.Tailscale, dns, port, false));
        foreach (var b in _bound.Where(b => b.Kind == PathKind.Tailnet))
            list.Add(new MobileEndpoint(MobileTransport.Tailscale, b.Address.ToString(), port, false));
        if (_bound.Any(b => b.Kind == PathKind.Loopback))
            list.Add(new MobileEndpoint(MobileTransport.Loopback, IPAddress.Loopback.ToString(), port, false));
        return list;
    }

    private IReadOnlyList<Binding> DesiredBindings(MobileServerOptions options)
    {
        var list = new List<Binding>();
        if (options.Tailscale)
            list.AddRange(MobileNetwork.FindTailnetAddresses().Select(a => new Binding(a, PathKind.Tailnet, 32)));
        if (options.Lan && EnsureLanCertificate())
            list.AddRange(MobileNetwork.FindLanAddresses().Select(a => new Binding(a.Address, PathKind.Lan, a.PrefixLength)));
        if (options.BindLoopback) list.Add(new Binding(IPAddress.Loopback, PathKind.Loopback, 8));
        return list;
    }

    private bool EnsureLanCertificate()
    {
        lock (_gate)
        {
            if (_lanCertificate is not null) return true;
        }
        try
        {
            var cert = MobileTlsCertificate.LoadOrCreate();
            lock (_gate)
            {
                _lanCertificate = cert;
                _lanPin = MobileTlsCertificate.Sha256Hex(cert);
            }
            return true;
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            SetError("LAN TLS certificate unavailable: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 바인딩 → 수락 → 30초마다 주소 집합 확인 → 바뀌면 다시 바인딩. 실패하면 30초 뒤 다시 시도한다.
    /// 포트가 이미 쓰이고 있으면(다른 프로그램, 같은 앱 중복 실행) 상태 문구로 알린다.
    /// </summary>
    private async Task RunAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = Options;
            var bind = DesiredBindings(options);
            if (bind.Count == 0)
            {
                SetError(null);
                await DelaySafe(RebindInterval, stoppingToken);
                continue;
            }

            var magicDns = bind.Any(b => b.Kind == PathKind.Tailnet) ? MobileNetwork.FindMagicDnsName() : null;
            using var cycle = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var listeners = new List<(TcpListener Listener, Binding Binding)>();
            try
            {
                foreach (var b in bind)
                {
                    var l = new TcpListener(b.Address, options.Port);
                    l.Start();
                    listeners.Add((l, b));
                }
                lock (_gate)
                {
                    // Stop→Start 직후에는 이전 루프가 아직 정리 중일 수 있다. 현재 루프만 상태를 바꾼다.
                    if (!IsCurrent(stoppingToken)) break;
                    _bound = bind;
                    _magicDns = magicDns;
                    _listening = true;
                    _lastError = null;
                }
                StatusChanged?.Invoke();

                var accepts = listeners.Select(l => AcceptLoopAsync(l.Listener, l.Binding.Kind, cycle.Token)).ToList();
                var keys = bind.Select(b => b.Key).ToHashSet();
                while (!stoppingToken.IsCancellationRequested)
                {
                    await DelaySafe(RebindInterval, stoppingToken);
                    if (!DesiredBindings(options).Select(b => b.Key).ToHashSet().SetEquals(keys)) break;
                }
                cycle.Cancel();
                foreach (var l in listeners) l.Listener.Stop();
                await Task.WhenAll(accepts);
            }
            catch (SocketException ex)
            {
                foreach (var l in listeners) l.Listener.Stop();
                SetError($"Port {options.Port}: {ex.SocketErrorCode}");
                await DelaySafe(RebindInterval, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                foreach (var l in listeners) l.Listener.Stop();
                SetError(ex.Message);
                await DelaySafe(RebindInterval, stoppingToken);
            }
            finally
            {
                foreach (var l in listeners) l.Listener.Stop();
                lock (_gate)
                {
                    if (IsCurrent(stoppingToken)) _listening = false;
                }
            }
        }
    }

    private bool IsCurrent(CancellationToken token) => _cts is { } cts && !cts.IsCancellationRequested && cts.Token == token;

    private void SetError(string? message)
    {
        lock (_gate) _lastError = message;
        StatusChanged?.Invoke();
    }

    private async Task AcceptLoopAsync(TcpListener listener, PathKind kind, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }

            if (!await _connections.WaitAsync(0, ct))
            {
                client.Dispose();
                continue;
            }
            _ = Task.Run(async () =>
            {
                try { await HandleConnectionAsync(client, kind, ct); }
                catch (Exception ex)
                {
                    // 연결 하나의 실패(끊김·TLS 실패·잘못된 요청)는 그 연결만 닫는다.
                    GC.KeepAlive(ex);
                }
                finally
                {
                    client.Dispose();
                    _connections.Release();
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>
    /// 바인딩 주소·방화벽 규칙 뒤의 마지막 방어선: 각 리스너는 그 경로에서 올 수 있는 상대만 받는다.
    /// (LAN 리스너는 같은 서브넷의 사설 주소만 — 공유기 포트포워딩 등으로 밖에서 들어온 연결을 막는다.)
    /// </summary>
    private bool PeerAllowed(IPAddress peer, PathKind kind)
    {
        if (peer.IsIPv4MappedToIPv6) peer = peer.MapToIPv4();
        switch (kind)
        {
            case PathKind.Tailnet:
                return MobileNetwork.IsTailnetV4(peer) || IPAddress.IsLoopback(peer);
            case PathKind.Lan:
                IReadOnlyList<Binding> bound;
                lock (_gate) bound = _bound;
                return MobileNetwork.IsPrivateV4(peer) && MobileNetwork.InSameSubnet(peer,
                    bound.Where(b => b.Kind == PathKind.Lan).Select(b => new LanAddress(b.Address, b.PrefixLength)));
            default:
                return IPAddress.IsLoopback(peer);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, PathKind kind, CancellationToken ct)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote || !PeerAllowed(remote.Address, kind))
            return;

        var network = client.GetStream();
        if (kind != PathKind.Lan)
        {
            await ServeAsync(network, ct);
            return;
        }

        X509Certificate2? cert;
        lock (_gate) cert = _lanCertificate;
        await using var ssl = new SslStream(network, leaveInnerStreamOpen: false);
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            handshake.CancelAfter(HeadTimeout);
            await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                ClientCertificateRequired = false,
            }, handshake.Token);
        }
        await ServeAsync(ssl, ct);
    }

    private async Task ServeAsync(Stream stream, CancellationToken ct)
    {
        (HttpRequestHead Head, byte[] Leftover)? request;
        using (var headCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            headCts.CancelAfter(HeadTimeout);
            request = await MiniHttp.ReadHeadAsync(stream, headCts.Token);
        }
        if (request is not { } r)
        {
            await MiniHttp.WriteResponseAsync(stream, 400, Error("bad_request", "Malformed request."), ct);
            return;
        }

        if (r.Head.Path == "/api/v1/live" && r.Head.Method == "GET")
        {
            await HandleLiveAsync(stream, r.Head, ct);
            return;
        }

        byte[]? body = null;
        if (r.Head.Method == "POST")
        {
            body = await MiniHttp.ReadBodyAsync(stream, r.Head, r.Leftover, ct);
            if (body is null)
            {
                await MiniHttp.WriteResponseAsync(stream, 400, Error("bad_request", "Missing or oversized body."), ct);
                return;
            }
        }

        var (status, json) = HandleRest(r.Head, body);
        await MiniHttp.WriteResponseAsync(stream, status, json, ct);
    }

    /// <summary>
    /// Input : 요청 머리, 본문
    /// Output: (상태 코드, JSON)
    /// 핵심 로직: 소켓과 분리해 두어 테스트가 라우팅·인증을 직접 확인할 수 있게 했다.
    ///           <c>/info</c> 만 인증 없이 열린다(PC 이름·버전) — 휴대폰 목록의 "온라인" 표시용이다.
    /// </summary>
    public (int Status, string Json) HandleRest(HttpRequestHead head, byte[]? body)
    {
        switch (head.Method, head.Path)
        {
            case ("GET", "/api/v1/info"):
                return (200, Json(new PcInfo(MobileProtocol.Schema, MobileProtocol.Protocol, _devices.PcId,
                    _pcName, AppVersion, Environment.OSVersion.VersionString)));

            case ("POST", "/api/v1/pair"):
            {
                PairRequest? req;
                try { req = JsonSerializer.Deserialize<PairRequest>(body!, MobileProtocol.Json); }
                catch (JsonException) { req = null; }
                if (req is null) return (400, Error("bad_request", "Invalid pairing request."));

                var result = _pairing.TryPair(req.Code, req.DeviceName, req.DeviceModel);
                return result.Outcome switch
                {
                    PairOutcome.Paired => (200, Json(new PairResponse(MobileProtocol.Schema, result.DeviceId!, result.Token!,
                        _devices.PcId, _pcName))),
                    PairOutcome.Expired => (410, Error("code_expired", "The pairing code expired. Show a new QR code.")),
                    PairOutcome.Locked => (429, Error("locked", "Too many wrong codes. Try again in a few minutes.")),
                    _ => (400, Error("invalid_code", "The pairing code is not valid.")),
                };
            }

            case ("GET", "/api/v1/endpoints"):
                if (_devices.Authenticate(head.BearerToken() ?? "") is null) return Unauthorized();
                return (200, Json(new EndpointsResponse(MobileProtocol.Schema, Endpoints())));

            case ("GET", "/api/v1/usage"):
                if (_devices.Authenticate(head.BearerToken() ?? "") is null) return Unauthorized();
                return (200, Json(BuildSnapshot()));

            case (_, "/api/v1/info" or "/api/v1/pair" or "/api/v1/endpoints" or "/api/v1/usage" or "/api/v1/live"):
                return (405, Error("method_not_allowed", "Method not allowed."));

            default:
                return (404, Error("not_found", "Not found."));
        }
    }

    public UsageSnapshotMessage BuildSnapshot() =>
        new(MobileProtocol.Schema, DateTimeOffset.UtcNow, _devices.PcId, _pcName, AppVersion, _feed.Current(_devices.PcId));

    /// <summary>
    /// WebSocket 실시간 전송. 연결 직후 한 번, 그 뒤 사용량이 바뀔 때마다, 변화가 없으면 <see cref="LiveHeartbeat"/> 마다 보낸다.
    /// 전송 전마다 페어링 상태를 다시 확인해 트레이에서 해제한 휴대폰을 다음 전송 전에 1008 "revoked" 로 끊는다.
    /// </summary>
    private async Task HandleLiveAsync(Stream stream, HttpRequestHead head, CancellationToken ct)
    {
        var device = _devices.Authenticate(head.BearerToken() ?? "");
        if (device is null)
        {
            var (status, json) = Unauthorized();
            await MiniHttp.WriteResponseAsync(stream, status, json, ct);
            return;
        }
        if (!head.IsWebSocketUpgrade)
        {
            await MiniHttp.WriteResponseAsync(stream, 400, Error("bad_request", "WebSocket upgrade required."), ct);
            return;
        }

        await MiniHttp.WriteUpgradeAsync(stream, head.Header("Sec-WebSocket-Key")!, ct);
        using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions
        {
            IsServer = true,
            KeepAliveInterval = TimeSpan.FromSeconds(15),
        });
        using var sub = _feed.Subscribe();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // 클라이언트 프레임을 비워야 상대의 close(또는 죽은 연결)를 곧바로 알아챈다.
        var receive = Task.Run(async () =>
        {
            var buf = new byte[256];
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var res = await socket.ReceiveAsync(buf, linked.Token);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException) { }
            finally { linked.Cancel(); }
        }, CancellationToken.None);

        try
        {
            do
            {
                if (!_devices.IsPaired(device.DeviceId))
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, RevokedCloseReason, CancellationToken.None);
                    break;
                }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(BuildSnapshot(), MobileProtocol.Json);
                await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, linked.Token);
            }
            while (await sub.WaitAsync(LiveHeartbeat, linked.Token));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is WebSocketException or IOException) { }
        finally
        {
            linked.Cancel();
            if (socket.State == WebSocketState.Open)
            {
                try { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch (Exception ex) when (ex is WebSocketException or IOException) { }
            }
            await receive;
        }
    }

    private static (int, string) Unauthorized() =>
        (401, Error("unauthorized", "This phone is not paired with this PC."));

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, MobileProtocol.Json);

    private static string Error(string code, string message) =>
        Json(new ErrorResponse(MobileProtocol.Schema, code, message));

    private static async Task DelaySafe(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }

    /// <summary>
    /// 서버를 멈춘다. <see cref="_connections"/> 는 일부러 Dispose 하지 않는다 — 끝나 가는 연결 작업이 finally 에서
    /// Release 를 부르므로, 먼저 Dispose 하면 그 작업들이 ObjectDisposedException 을 낸다. SemaphoreSlim 은
    /// AvailableWaitHandle 을 쓰지 않는 한 비관리 자원을 잡지 않는다.
    /// </summary>
    public void Dispose() => Stop();
}
