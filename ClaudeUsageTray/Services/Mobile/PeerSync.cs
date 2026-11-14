using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Models;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// PC 동기화 프레임(#223). 공유 폴더 동기화가 파일로 주고받던 <see cref="UsageSyncSnapshot"/> 을 그대로 싣는다 —
/// 같은 형식·같은 계정 해시라서, 받은 쪽은 기존 "어느 PC 값을 쓸지" 선택 규칙(SelectQuotaCandidates·MergeLocalTotals)을
/// 손대지 않고 그대로 쓴다.
/// </summary>
public sealed record SyncFrame(int Schema, string Kind, string FromPcId, string FromPcName, IReadOnlyList<UsageSyncSnapshot> Snapshots)
{
    public const string FrameKind = "sync";
}

/// <summary>
/// 이 PC 가 아는 모든 PC 의 최신 스냅샷(자기 것 포함)을 들고 있다가, 바뀌면 연결된 모든 PC 에 다시 전한다(가십).
///
/// 왜 가십인가: A↔B, A↔C 처럼 한 PC 에만 연결해도 B 와 C 가 서로의 값을 받는다. 키를 (보낸 원래 PC, provider)로
/// 두고 관측 시각이 더 새로울 때만 바꾸므로, 고리 모양으로 연결돼도 같은 값이 계속 돌지 않고 멈춘다.
/// </summary>
public sealed class PeerSyncHub
{
    /// <summary>
    /// 이보다 오래된 관측은 버린다. 공유 폴더 동기화의 최후 폴백 기준(<see cref="UsageSyncService.LastResortMaxAge"/>)과 같다.
    /// </summary>
    public static readonly TimeSpan MaxAge = UsageSyncService.LastResortMaxAge;

    private readonly object _gate = new();
    private readonly Dictionary<(string Device, string Provider), UsageSyncSnapshot> _table = [];
    private readonly Func<DateTimeOffset> _now;
    private readonly List<Action> _listeners = [];
    private string _ownDeviceId = "";

    public PeerSyncHub(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>표에 바뀐 것이 있을 때(연결된 PC 에 다시 보내야 할 때) 알린다.</summary>
    public IDisposable OnChanged(Action listener)
    {
        lock (_gate) _listeners.Add(listener);
        return new Unsubscribe(this, listener);
    }

    /// <summary>
    /// 이 PC 가 방금 만든 스냅샷. 공유 폴더에 쓰던 것과 같은 객체를 넣는다.
    /// </summary>
    public void PublishLocal(UsageSyncSnapshot snapshot)
    {
        lock (_gate) _ownDeviceId = snapshot.DeviceId;
        if (Upsert(snapshot, local: true)) Notify();
    }

    /// <summary>
    /// Input : 다른 PC 가 보낸 스냅샷 묶음
    /// Output: 표가 바뀌었는가(바뀌었으면 다른 연결에도 전한다)
    /// 핵심 로직: 이 PC 자신의 기록은 받지 않는다 — 내 값의 원본은 나다. 다른 PC 를 거쳐 돌아온 옛 값이 덮어쓰지 않게.
    /// </summary>
    public bool Merge(IEnumerable<UsageSyncSnapshot> snapshots)
    {
        var changed = false;
        foreach (var s in snapshots)
        {
            if (!IsUsable(s)) continue;
            string own;
            lock (_gate) own = _ownDeviceId;
            if (string.Equals(s.DeviceId, own, StringComparison.OrdinalIgnoreCase)) continue;
            changed |= Upsert(s, local: false);
        }
        if (changed) Notify();
        return changed;
    }

    /// <summary>보낼 표 전체(오래된 것은 뺀다).</summary>
    public IReadOnlyList<UsageSyncSnapshot> Snapshot()
    {
        var cutoff = _now() - MaxAge;
        lock (_gate)
        {
            return _table.Values.Where(s => s.ObservedAtUtc >= cutoff).ToList();
        }
    }

    /// <summary>다른 PC 들의 스냅샷 중 이 provider·계정·날짜에 해당하는 것(공유 폴더의 ReadSnapshots 와 같은 조건).</summary>
    public IReadOnlyList<UsageSyncSnapshot> Read(string provider, string accountHash, DateOnly localDate)
    {
        var date = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string own;
        lock (_gate) own = _ownDeviceId;
        return Snapshot()
            .Where(s => !string.Equals(s.DeviceId, own, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Provider == provider && s.AccountHash == accountHash && s.LocalDate == date)
            .ToList();
    }

    /// <summary>
    /// 이 PC 에 그 provider 계정이 없을 때(예: Claude Code 를 설치하지 않은 PC) 대신 보여 줄 계정.
    /// 그룹 안에서 할당량을 가장 최근에 관측한 계정을 고른다 — 보통 그룹에 계정이 하나뿐이고, 여럿이면 지금 쓰는 쪽이다.
    /// </summary>
    public string? MostRecentAccountHash(string provider)
    {
        string own;
        lock (_gate) own = _ownDeviceId;
        return Snapshot()
            .Where(s => s.Provider == provider && !string.Equals(s.DeviceId, own, StringComparison.OrdinalIgnoreCase))
            .Where(s => s.Quota is { HasData: true })
            .OrderByDescending(s => s.Quota!.ObservedAtUtc ?? s.ObservedAtUtc)
            .Select(s => s.AccountHash)
            .FirstOrDefault();
    }

    /// <summary>다른 PC 에서 받은 기록이 하나라도 있는가(상태 표시용).</summary>
    public int RemoteDeviceCount
    {
        get
        {
            string own;
            lock (_gate) own = _ownDeviceId;
            return Snapshot().Select(s => s.DeviceId).Where(d => !string.Equals(d, own, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        }
    }

    private bool Upsert(UsageSyncSnapshot s, bool local)
    {
        var key = (s.DeviceId.ToLowerInvariant(), s.Provider);
        lock (_gate)
        {
            if (_table.TryGetValue(key, out var existing) && !local && existing.ObservedAtUtc >= s.ObservedAtUtc)
                return false;
            _table[key] = s;
            return true;
        }
    }

    /// <summary>외부 입력 검사 — 형식이 틀리거나 미래(5분 넘게)·너무 오래된 관측은 받지 않는다.</summary>
    private bool IsUsable(UsageSyncSnapshot? s)
    {
        if (s is null || s.SchemaVersion != UsageSyncSchema.CurrentVersion) return false;
        if (string.IsNullOrWhiteSpace(s.DeviceId) || string.IsNullOrWhiteSpace(s.Provider) || string.IsNullOrWhiteSpace(s.AccountHash))
            return false;
        var now = _now();
        if (s.ObservedAtUtc > now + TimeSpan.FromMinutes(5) || s.ObservedAtUtc < now - MaxAge) return false;
        s.LocalTotals ??= new UsageSyncLocalTotals();
        s.LocalTotals.HourlyTokens = s.LocalTotals.HourlyTokens is { Length: 24 } h ? h : new long[24];
        if (s.Quota is { } q)
        {
            q.Models ??= [];
            q.AdditionalQuotas ??= [];
        }
        return true;
    }

    private void Notify()
    {
        Action[] listeners;
        lock (_gate) listeners = [.. _listeners];
        foreach (var l in listeners)
        {
            try { l(); } catch (Exception ex) { GC.KeepAlive(ex); }
        }
    }

    private sealed class Unsubscribe(PeerSyncHub hub, Action listener) : IDisposable
    {
        public void Dispose()
        {
            lock (hub._gate) hub._listeners.Remove(listener);
        }
    }
}

/// <summary>이 PC 가 동기화 코드로 연결해 둔 다른 PC(이 PC 가 연결을 여는 쪽).</summary>
public sealed record SyncPeer(string PcId, string PcName, IReadOnlyList<MobileEndpoint> Endpoints, string DeviceId, string Token, DateTimeOffset AddedAt);

/// <summary>
/// 연결해 둔 PC 목록을 <c>%APPDATA%\ClaudeUsageTray\mobile\peers.json</c> 에 둔다. 토큰은 Windows DPAPI(현재 사용자)로
/// 감싼다 — 파일이 다른 사용자·다른 PC 로 복사돼도 그 PC 에 붙을 수 없게.
/// </summary>
public sealed class SyncPeerStore
{
    private static readonly JsonSerializerOptions FileJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();
    private List<SyncPeer> _peers;

    public event Action? Changed;

    public SyncPeerStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? MobileDeviceStore.DefaultDirectory, "peers.json");
        _peers = Load();
    }

    public IReadOnlyList<SyncPeer> All()
    {
        lock (_gate) return [.. _peers];
    }

    public void Upsert(SyncPeer peer)
    {
        lock (_gate)
        {
            _peers.RemoveAll(p => p.PcId == peer.PcId);
            _peers.Add(peer);
            Save();
        }
        Changed?.Invoke();
    }

    public void UpdateEndpoints(string pcId, IReadOnlyList<MobileEndpoint> endpoints)
    {
        if (endpoints.Count == 0) return;
        lock (_gate)
        {
            var i = _peers.FindIndex(p => p.PcId == pcId);
            if (i < 0 || _peers[i].Endpoints.SequenceEqual(endpoints)) return;
            _peers[i] = _peers[i] with { Endpoints = endpoints };
            Save();
        }
    }

    public bool Remove(string pcId)
    {
        bool removed;
        lock (_gate)
        {
            removed = _peers.RemoveAll(p => p.PcId == pcId) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke();
        return removed;
    }

    private List<SyncPeer> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var stored = JsonSerializer.Deserialize<List<StoredPeer>>(stream, FileJson) ?? [];
            return stored.Select(s => s.ToPeer()).OfType<SyncPeer>().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, $"peers.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite))
            {
                JsonSerializer.Serialize(stream, _peers.Select(StoredPeer.From).ToList(), FileJson);
            }
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GC.KeepAlive(ex);
        }
    }

    private sealed class StoredPeer
    {
        public string PcId { get; set; } = "";
        public string PcName { get; set; } = "";
        public List<MobileEndpoint> Endpoints { get; set; } = [];
        public string DeviceId { get; set; } = "";
        public string ProtectedToken { get; set; } = "";
        public DateTimeOffset AddedAt { get; set; }

        public static StoredPeer From(SyncPeer p) => new()
        {
            PcId = p.PcId,
            PcName = p.PcName,
            Endpoints = [.. p.Endpoints],
            DeviceId = p.DeviceId,
            ProtectedToken = Protect(p.Token),
            AddedAt = p.AddedAt,
        };

        public SyncPeer? ToPeer()
        {
            var token = Unprotect(ProtectedToken);
            return token is null || string.IsNullOrEmpty(PcId) ? null
                : new SyncPeer(PcId, PcName, Endpoints, DeviceId, token, AddedAt);
        }
    }

    private static string Protect(string token) =>
        OperatingSystem.IsWindows()
            ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser))
            : "plain:" + token; // 테스트(리눅스) 전용 — 앱은 Windows 에서만 돈다.

    private static string? Unprotect(string stored)
    {
        try
        {
            if (stored.StartsWith("plain:", StringComparison.Ordinal)) return stored["plain:".Length..];
            if (!OperatingSystem.IsWindows()) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null; // 다른 사용자·PC 에서 옮겨 온 파일 — 다시 연결해야 한다.
        }
    }
}

/// <summary>연결해 둔 PC 하나의 지금 상태(설정 창 표시용).</summary>
public sealed record SyncPeerStatus(SyncPeer Peer, bool Connected, string? Via, string? LastError);

/// <summary>
/// 동기화 코드로 다른 PC 에 연결하고, 연결해 둔 PC 마다 양방향 WebSocket(<c>/api/v1/sync</c>)을 유지한다(#223).
/// 이 PC 가 여는 쪽이라, 상대 PC 만 이 PC 에서 닿으면 된다(이 PC 의 포트는 열려 있지 않아도 된다).
/// 경로 선택은 휴대폰 앱과 같다: LAN(TLS 인증서 핀) → Tailscale.
/// </summary>
public sealed class PeerSyncClient : IDisposable
{
    private static readonly int[] BackoffSeconds = [2, 5, 15, 30, 60];
    private const int MaxFrameBytes = 1024 * 1024;

    private readonly PeerSyncHub _hub;
    private readonly SyncPeerStore _peers;
    private readonly Func<string> _pcId;
    private readonly string _pcName;
    private readonly object _gate = new();
    private readonly Dictionary<string, (CancellationTokenSource Cts, SyncPeerStatus Status)> _links = [];

    public event Action? Changed;

    public PeerSyncClient(PeerSyncHub hub, SyncPeerStore peers, Func<string> pcId, string? pcName = null)
    {
        _hub = hub;
        _peers = peers;
        _pcId = pcId;
        _pcName = string.IsNullOrWhiteSpace(pcName) ? Environment.MachineName : pcName;
        _peers.Changed += Reconcile;
    }

    public IReadOnlyList<SyncPeerStatus> Status()
    {
        lock (_gate)
        {
            return _peers.All().Select(p => _links.TryGetValue(p.PcId, out var l) ? l.Status with { Peer = p }
                : new SyncPeerStatus(p, false, null, null)).ToList();
        }
    }

    /// <summary>저장된 PC 목록과 실행 중인 연결을 맞춘다(추가된 PC 는 연결, 지운 PC 는 끊기).</summary>
    public void Reconcile()
    {
        var wanted = _peers.All().ToDictionary(p => p.PcId);
        lock (_gate)
        {
            foreach (var gone in _links.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
            {
                _links[gone].Cts.Cancel();
                _links.Remove(gone);
            }
            foreach (var peer in wanted.Values.Where(p => !_links.ContainsKey(p.PcId)))
            {
                var cts = new CancellationTokenSource();
                _links[peer.PcId] = (cts, new SyncPeerStatus(peer, false, null, null));
                var pcId = peer.PcId;
                _ = Task.Run(() => RunAsync(pcId, cts.Token));
            }
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Input : 상대 PC 의 동기화 코드(페어링 링크 또는 오퍼 JSON)
    /// Output: 연결한 PC 이름 — 실패하면 예외(<see cref="PeerJoinException"/>: 코드 문제, 그 밖: 닿지 않음)
    /// 핵심 로직: 휴대폰과 같은 <c>POST /api/v1/pair</c> 에 <c>kind: "pc"</c> 를 실어 보낸다. 코드는 1회용이라 첫 응답이
    ///           거절이면 다른 경로로 다시 시도하지 않는다(같은 코드로 다시 보내면 상대 PC 의 실패 횟수만 쌓인다).
    /// </summary>
    public async Task<string> JoinAsync(string code, CancellationToken ct = default)
    {
        var offer = ParseOffer(code) ?? throw new PeerJoinException("not_a_code");
        if (offer.PcId == _pcId()) throw new PeerJoinException("self");

        var body = JsonSerializer.Serialize(new PairRequest(offer.Code, _pcName, "Windows PC", MobileDeviceKind.Pc), MobileProtocol.Json);
        Exception? last = null;
        foreach (var e in Order(offer.Endpoints))
        {
            using var http = CreateHttp(e, TimeSpan.FromSeconds(6));
            HttpResponseMessage response;
            try
            {
                response = await http.PostAsync(Url(e, "/api/v1/pair"), new StringContent(body, Encoding.UTF8, "application/json"), ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                continue;
            }
            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    var error = TryReadError(text) ?? $"http_{(int)response.StatusCode}";
                    throw new PeerJoinException(error);
                }
                var pair = JsonSerializer.Deserialize<PairResponse>(text, MobileProtocol.Json)
                    ?? throw new PeerJoinException("bad_response");
                _peers.Upsert(new SyncPeer(pair.PcId, pair.PcName, offer.Endpoints, pair.DeviceId, pair.Token, DateTimeOffset.UtcNow));
                return pair.PcName;
            }
        }
        throw new IOException(last?.Message ?? "no endpoints", last);
    }

    /// <summary>연결을 끊고 목록에서 지운다. 상대 PC 의 기기 목록에는 남으므로 그쪽에서도 해제할 수 있다.</summary>
    public void Remove(string pcId) => _peers.Remove(pcId);

    private async Task RunAsync(string pcId, CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            var peer = _peers.All().FirstOrDefault(p => p.PcId == pcId);
            if (peer is null) return;
            string? error = null;
            foreach (var e in Order(peer.Endpoints))
            {
                try
                {
                    await RunSocketAsync(peer, e, ct, () => attempt = 0);
                    error = "closed";
                    break;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    SetStatus(pcId, false, null, "revoked");
                    return; // 상대 PC 에서 해제됨 — 다시 연결해야 한다.
                }
                catch (Exception ex) when (ex is WebSocketException or IOException or HttpRequestException or System.Security.Authentication.AuthenticationException)
                {
                    error = $"{e.Transport}: {ex.Message}";
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }
            SetStatus(pcId, false, null, error);
            var wait = BackoffSeconds[Math.Min(attempt++, BackoffSeconds.Length - 1)];
            try { await Task.Delay(TimeSpan.FromSeconds(wait), ct); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>
    /// 한 경로로 붙어 있는 동안: 표 전체를 보내고, 바뀔 때마다(또는 60초마다) 다시 보내며, 받은 표는 합친다.
    /// </summary>
    private async Task RunSocketAsync(SyncPeer peer, MobileEndpoint e, CancellationToken ct, Action onConnected)
    {
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {peer.Token}");
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        if (e.CertSha256 is { } pin)
            ws.Options.RemoteCertificateValidationCallback = (_, cert, _, _) => PinMatches(cert, pin);
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(6));
        try
        {
            await ws.ConnectAsync(new Uri(Url(e, "/api/v1/sync").Replace("http", "ws", StringComparison.Ordinal)), connectCts.Token);
        }
        catch (WebSocketException ex) when (ex.Message.Contains("401", StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("revoked", ex);
        }
        onConnected();
        SetStatus(peer.PcId, true, e.Transport, null);
        _ = RefreshEndpointsAsync(peer, e);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var signal = new SemaphoreSlim(0, 1);
        using var subscription = _hub.OnChanged(() => { if (signal.CurrentCount == 0) signal.Release(); });
        var receive = ReceiveLoopAsync(ws, peer, linked.Token);
        try
        {
            while (!linked.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                await SendAsync(ws, linked.Token);
                var waitTask = signal.WaitAsync(TimeSpan.FromSeconds(60), linked.Token);
                if (await Task.WhenAny(waitTask, receive) == receive) break;
            }
        }
        finally
        {
            linked.Cancel();
            try { await receive; } catch (Exception ex) { GC.KeepAlive(ex); }
            if (ws.State == WebSocketState.Open)
            {
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None); }
                catch (Exception ex) when (ex is WebSocketException or IOException) { }
            }
        }
        if (ws.CloseStatus == WebSocketCloseStatus.PolicyViolation)
            throw new UnauthorizedAccessException("revoked");
    }

    private async Task SendAsync(WebSocket ws, CancellationToken ct)
    {
        var frame = new SyncFrame(MobileProtocol.Schema, SyncFrame.FrameKind, _pcId(), _pcName, _hub.Snapshot());
        await ws.SendAsync(JsonSerializer.SerializeToUtf8Bytes(frame, MobileProtocol.Json), WebSocketMessageType.Text, true, ct);
    }

    private async Task ReceiveLoopAsync(WebSocket ws, SyncPeer peer, CancellationToken ct)
    {
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var bytes = await ReceiveMessageAsync(ws, ct);
            if (bytes is null) return;
            ApplyFrame(_hub, bytes);
            SetStatus(peer.PcId, true, null, null);
        }
    }

    /// <summary>받은 프레임을 표에 합친다. 형식이 틀린 프레임은 버린다(연결은 유지).</summary>
    public static bool ApplyFrame(PeerSyncHub hub, byte[] bytes)
    {
        try
        {
            var frame = JsonSerializer.Deserialize<SyncFrame>(bytes, MobileProtocol.Json);
            if (frame is null || frame.Schema != MobileProtocol.Schema || frame.Kind != SyncFrame.FrameKind) return false;
            return hub.Merge(frame.Snapshots ?? []);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>한 메시지를 끝까지 읽는다(최대 1MB). 상대가 닫으면 null.</summary>
    public static async Task<byte[]?> ReceiveMessageAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (ms.Length > MaxFrameBytes) throw new IOException("sync frame too large");
            if (result.EndOfMessage) return ms.ToArray();
        }
    }

    private async Task RefreshEndpointsAsync(SyncPeer peer, MobileEndpoint e)
    {
        try
        {
            using var http = CreateHttp(e, TimeSpan.FromSeconds(6));
            using var req = new HttpRequestMessage(HttpMethod.Get, Url(e, "/api/v1/endpoints"));
            req.Headers.Authorization = new("Bearer", peer.Token);
            using var res = await http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return;
            var body = JsonSerializer.Deserialize<EndpointsResponse>(await res.Content.ReadAsStringAsync(), MobileProtocol.Json);
            if (body?.Endpoints is { Count: > 0 } endpoints) _peers.UpdateEndpoints(peer.PcId, endpoints);
        }
        catch (Exception ex)
        {
            GC.KeepAlive(ex); // 경로 갱신은 부가 기능 — 실패해도 동기화는 계속된다.
        }
    }

    private void SetStatus(string pcId, bool connected, string? via, string? error)
    {
        lock (_gate)
        {
            if (!_links.TryGetValue(pcId, out var link)) return;
            var next = link.Status with { Connected = connected, Via = via ?? (connected ? link.Status.Via : null), LastError = error };
            if (next == link.Status) return;
            _links[pcId] = (link.Cts, next);
        }
        Changed?.Invoke();
    }

    /// <summary>LAN 을 먼저(같은 집·사무실이면 가장 빠름), 그다음 Tailscale. 이 PC 에서 PC 로 가므로 네트워크 힌트 없이 둘 다 시도한다.</summary>
    internal static IEnumerable<MobileEndpoint> Order(IReadOnlyList<MobileEndpoint> endpoints) =>
        endpoints.OrderBy(e => e.Transport switch { MobileTransport.Lan => 0, MobileTransport.Tailscale => 1, _ => 2 });

    internal static string Url(MobileEndpoint e, string path)
    {
        var host = e.Host.Contains(':') && !e.Host.StartsWith('[') ? $"[{e.Host}]" : e.Host;
        return $"{(e.Tls ? "https" : "http")}://{host}:{e.Port}{path}";
    }

    private static HttpClient CreateHttp(MobileEndpoint e, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler { ConnectTimeout = timeout };
        if (e.CertSha256 is { } pin)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, _) => PinMatches(cert, pin);
        return new HttpClient(handler) { Timeout = timeout };
    }

    /// <summary>LAN 은 자체 서명 인증서라 CA 대신 동기화 코드에 담긴 SHA-256 과 같은지만 본다(휴대폰 앱과 같은 핀 방식).</summary>
    internal static bool PinMatches(X509Certificate? cert, string pin) =>
        cert is not null && string.Equals(Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())), pin, StringComparison.OrdinalIgnoreCase);

    /// <summary>동기화 코드 = 휴대폰용 QR 과 같은 페어링 링크(또는 그 JSON). 앞뒤 공백·줄바꿈은 무시한다.</summary>
    public static PairingOffer? ParseOffer(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var text = string.Concat(code.Where(c => !char.IsWhiteSpace(c)));
        var json = PairingLink.DecodeJson(text) ?? (text.StartsWith('{') ? text : null);
        if (json is null) return null;
        try
        {
            var offer = JsonSerializer.Deserialize<PairingOffer>(json, MobileProtocol.Json);
            return offer is { Cu: MobileProtocol.OfferMarker } && !string.IsNullOrEmpty(offer.Code)
                && offer.Endpoints is { Count: > 0 } ? offer : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadError(string text)
    {
        try { return JsonSerializer.Deserialize<ErrorResponse>(text, MobileProtocol.Json)?.Error; }
        catch (JsonException) { return null; }
    }

    public void Dispose()
    {
        _peers.Changed -= Reconcile;
        lock (_gate)
        {
            foreach (var link in _links.Values) link.Cts.Cancel();
            _links.Clear();
        }
    }
}

/// <summary>동기화 코드가 거절됐다 — <see cref="Exception.Message"/> 는 상대 PC 의 오류 코드(invalid_code·code_expired·locked 등).</summary>
public sealed class PeerJoinException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
