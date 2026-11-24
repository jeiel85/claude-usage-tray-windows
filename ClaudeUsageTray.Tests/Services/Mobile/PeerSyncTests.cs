using System.IO;
using System.Net;
using System.Net.Sockets;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// PC 직접 동기화(#223)를 루프백 소켓으로 끝까지 확인한다. PC 세 대(A·B·C)를 한 프로세스에 띄워
/// B·C 가 각각 A 의 동기화 코드로만 연결해도 B 가 C 의 값을 받는지(가십), 해제하면 끊기는지 본다.
/// </summary>
public class PeerSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cut-peer-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static UsageSyncSnapshot Snapshot(string deviceId, string device, string provider, string? accountKey, double shortPercent,
        DateTimeOffset? observed = null)
    {
        var at = observed ?? DateTimeOffset.Now;
        return new UsageSyncSnapshot
        {
            SchemaVersion = UsageSyncSchema.CurrentVersion,
            AccountHash = UsageSyncService.BuildAccountHash(provider, accountKey),
            Provider = provider,
            DeviceId = deviceId,
            DeviceName = device,
            LocalDate = DateOnly.FromDateTime(at.LocalDateTime).ToString("yyyy-MM-dd"),
            ObservedAtUtc = at.ToUniversalTime(),
            Quota = new UsageSyncQuotaSnapshot { HasData = true, ObservedAtUtc = at.ToUniversalTime(), ShortUsagePercent = shortPercent },
            LocalTotals = new UsageSyncLocalTotals { InputTokens = 100 },
        };
    }

    // 계정 별명(#313)은 스냅샷의 선택 필드로 PC 직접 동기화 프레임을 그대로 지난다. 별명이 없는 스냅샷은 필드 자체를
    // 싣지 않고(예전 PC 와 같은 모양), 예전 PC 가 보낸 프레임은 별명 없이 읽힌다.
    [Fact]
    public void SyncFrameCarriesTheAccountAlias()
    {
        var at = new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);
        var withAlias = Snapshot("pc-a", "A", "claude", "org", 0.1);
        withAlias.AccountAlias = new UsageSyncAccountAlias { KeyHash = "abc", Alias = "회사", UpdatedAtUtc = at };
        var withoutAlias = Snapshot("pc-b", "B", "claude", "org", 0.2);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new SyncFrame(MobileProtocol.Schema, SyncFrame.FrameKind, "pc-a", "A", [withAlias, withoutAlias]), MobileProtocol.Json);

        var json = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(json, "\"accountAlias\""));

        var hub = new PeerSyncHub();
        hub.PublishLocal(Snapshot("me", "ME", "claude", "org", 0.0));
        Assert.NotNull(PeerSyncClient.ApplyFrame(hub, bytes));
        var read = hub.Read("claude", UsageSyncService.BuildAccountHash("claude", "org"), DateOnly.FromDateTime(DateTime.Now));
        var a = read.Single(s => s.DeviceId == "pc-a").AccountAlias!;
        Assert.Equal(("abc", "회사", at), (a.KeyHash, a.Alias, a.UpdatedAtUtc));
        Assert.Null(read.Single(s => s.DeviceId == "pc-b").AccountAlias);
    }

    [Fact]
    public void HubKeepsNewestPerDeviceAndIgnoresItsOwnEcho()
    {
        var hub = new PeerSyncHub();
        hub.PublishLocal(Snapshot("me", "ME", "claude", "org", 0.1));
        var older = Snapshot("other", "OTHER", "claude", "org", 0.2, DateTimeOffset.Now.AddMinutes(-5));
        var newer = Snapshot("other", "OTHER", "claude", "org", 0.3);
        Assert.True(hub.Merge([newer]));
        Assert.False(hub.Merge([older])); // 늦게 돌아온 옛 값은 무시
        // 다른 PC 를 거쳐 돌아온 내 옛 값은 받지 않는다.
        Assert.False(hub.Merge([Snapshot("me", "ME", "claude", "org", 0.9)]));

        var today = DateOnly.FromDateTime(DateTime.Now);
        var read = hub.Read("claude", UsageSyncService.BuildAccountHash("claude", "org"), today);
        Assert.Equal(0.3, read.Single().Quota!.ShortUsagePercent);
        Assert.Equal(1, hub.RemoteDeviceCount);
    }

    [Fact]
    public void PcWithoutClaudeLoginBorrowsTheGroupsMostRecentAccount()
    {
        var hub = new PeerSyncHub();
        hub.Merge([
            Snapshot("a", "A", "claude", "org-1", 0.2, DateTimeOffset.Now.AddMinutes(-10)),
            Snapshot("b", "B", "claude", "org-2", 0.5, DateTimeOffset.Now.AddMinutes(-1)),
        ]);
        Assert.Equal(UsageSyncService.BuildAccountHash("claude", "org-2"), hub.MostRecentAccountHash("claude"));
        Assert.Null(hub.MostRecentAccountHash("codex"));
    }

    [Fact]
    public void RejectsMalformedAndFutureSnapshots()
    {
        var hub = new PeerSyncHub();
        var future = Snapshot("x", "X", "claude", "org", 0.1, DateTimeOffset.Now.AddHours(1));
        var wrongSchema = Snapshot("y", "Y", "claude", "org", 0.1);
        wrongSchema.SchemaVersion = 99;
        Assert.False(hub.Merge([future, wrongSchema]));
        Assert.Empty(hub.Snapshot());
    }

    [Fact]
    public void SyncCodeIsThePairingLinkAndToleratesWhitespace()
    {
        var link = PairingLink.Build(MobileProtocolFixtures.Offer());
        var offer = PeerSyncClient.ParseOffer("  " + link[..40] + "\n" + link[40..] + " ");
        Assert.NotNull(offer);
        Assert.Equal("HOME-PC", offer!.PcName);
        Assert.Null(PeerSyncClient.ParseOffer("hello"));
    }

    private sealed record Pc(string Name, MobileDeviceStore Store, PeerSyncHub Hub, MobileApiServer Server, PeerSyncClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    private Pc StartPc(string name)
    {
        var dir = Path.Combine(_dir, name);
        var store = new MobileDeviceStore(dir);
        var hub = new PeerSyncHub();
        var server = new MobileApiServer(new MobileUsageFeed(), store, new PairingService(store), () => name, hub);
        server.Start(new MobileServerOptions(FreePort(), Lan: false, Tailscale: false, BindLoopback: true));
        var client = new PeerSyncClient(hub, new SyncPeerStore(dir), () => store.PcId, () => name);
        return new Pc(name, store, hub, server, client);
    }

    [Fact]
    public async Task TwoPcsJoinOneHostAndGossipReachesBoth()
    {
        using var a = StartPc("A");
        using var b = StartPc("B");
        using var c = StartPc("C");
        await WaitUntil(() => a.Server.Status().Listening);

        // B, C 가 A 의 동기화 코드(=QR 과 같은 링크)로 연결한다. 코드는 1회용이라 각자 새로 받는다.
        Assert.Equal("A", await b.Client.JoinAsync(PairingLink.Build(a.Server.CreateOffer()!)));
        Assert.Equal("A", await c.Client.JoinAsync(PairingLink.Build(a.Server.CreateOffer()!)));
        Assert.Equal(2, a.Store.List().Count(d => d.Kind == MobileDeviceKind.Pc));

        // 같은 코드를 다시 쓰면 거절된다.
        var used = PairingLink.Build(a.Server.CreateOffer()!);
        await b.Client.JoinAsync(used);
        var ex = await Assert.ThrowsAsync<PeerJoinException>(() => c.Client.JoinAsync(used));
        Assert.Equal("invalid_code", ex.Code);

        b.Client.Reconcile();
        c.Client.Reconcile();
        await WaitUntil(() => b.Client.Status().All(s => s.Connected) && c.Client.Status().All(s => s.Connected));

        a.Hub.PublishLocal(Snapshot("dev-a", "A", "claude", "org", 0.1));
        b.Hub.PublishLocal(Snapshot("dev-b", "B", "claude", "org", 0.2));
        c.Hub.PublishLocal(Snapshot("dev-c", "C", "codex", null, 0.3));

        // B 는 A 에만 연결했지만 C 의 Codex 값을 A 를 거쳐 받는다.
        await WaitUntil(() => b.Hub.Snapshot().Any(s => s.DeviceId == "dev-c"));
        await WaitUntil(() => c.Hub.Snapshot().Any(s => s.DeviceId == "dev-b"));
        await WaitUntil(() => a.Hub.RemoteDeviceCount == 2);

        // A 에서 B 를 해제하면 B 의 연결이 끊기고 다시 붙지 않는다.
        // B 는 위에서 두 번 연결해(재사용 코드 확인) A 에 B 항목이 둘이다 — 지금 쓰는 것은 나중 토큰이므로 둘 다 해제한다.
        foreach (var bOnA in a.Store.List().Where(d => d.Kind == MobileDeviceKind.Pc && d.Name == "B"))
            a.Store.Revoke(bOnA.DeviceId);
        a.Hub.PublishLocal(Snapshot("dev-a", "A", "claude", "org", 0.15)); // 다음 전송을 일으킨다
        await WaitUntil(() => b.Client.Status().Any(s => s.LastError == "revoked"));

        // 새 코드로 다시 연결하면(토큰 교체) 끝난 연결 작업을 버리고 새로 붙는다(Codex 리뷰 P2).
        Assert.Equal("A", await b.Client.JoinAsync(PairingLink.Build(a.Server.CreateOffer()!)));
        await WaitUntil(() => b.Client.Status().Single().Connected);
    }

    /// <summary>
    /// 받는 쪽은 한 프레임에서 <see cref="PeerSyncHub.MaxSnapshotsPerFrame"/> 개만 합치므로(#340), 보내는 쪽은 그보다 큰 표를
    /// 나눠 보내야 한다(#342). 나누지 않으면 129번째부터는 재전송·하트비트로도 영영 닿지 않았다.
    /// 연결된 쪽(서버 → 클라이언트)과 연결한 쪽(클라이언트 → 서버) 두 방향을 따로 본다 — 합치면 표 상한(256)에 걸린다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TableLargerThanOneFrameReachesThePeerWhole(bool hostSends)
    {
        using var a = StartPc("A");
        using var b = StartPc("B");
        await WaitUntil(() => a.Server.Status().Listening);
        Assert.Equal("A", await b.Client.JoinAsync(PairingLink.Build(a.Server.CreateOffer()!)));
        b.Client.Reconcile();
        await WaitUntil(() => b.Client.Status().All(s => s.Connected));

        var (sender, receiver) = hostSends ? (a.Hub, b.Hub) : (b.Hub, a.Hub);
        var count = PeerSyncHub.MaxSnapshotsPerFrame + 72;
        var all = Enumerable.Range(0, count).Select(i => Snapshot($"far-{i}", $"FAR-{i}", "claude", "org", 0.1)).ToList();
        foreach (var chunk in all.Chunk(PeerSyncHub.MaxSnapshotsPerFrame))
            sender.Merge(chunk);
        Assert.Equal(count, sender.Snapshot().Count);

        await WaitUntil(() => receiver.Snapshot().Count == count);
    }

    [Fact]
    public void SplitKeepsEveryFrameWithinTheReceiveBound()
    {
        var all = Enumerable.Range(0, PeerSyncHub.MaxSnapshotsPerFrame * 2 + 1)
            .Select(i => Snapshot($"d-{i}", "D", "claude", "org", 0.1)).ToList();
        var frames = SyncFrame.Split("pc", "PC", all).ToList();
        Assert.Equal(3, frames.Count);
        Assert.All(frames, f => Assert.InRange(f.Snapshots.Count, 1, PeerSyncHub.MaxSnapshotsPerFrame));
        Assert.Equal(all.Select(s => s.DeviceId), frames.SelectMany(f => f.Snapshots).Select(s => s.DeviceId));

        // 빈 표도 프레임 하나는 보낸다 — 이름 변경(#271)이 그 프레임으로 간다.
        var empty = Assert.Single(SyncFrame.Split("pc", "PC", []));
        Assert.Empty(empty.Snapshots);
    }

    /// <summary>
    /// 연결한 쪽(B)에서 상대 PC(A)를 지우면 A 의 기기 목록에서도 B 가 빠진다(#285) — 남아 있으면 다시 연결할 때 B 가 두 번 보인다.
    /// 상대에게 닿지 않아도 이쪽 목록에서는 바로 지워지고 예외가 나지 않는다.
    /// </summary>
    [Fact]
    public async Task RemovingAPeerAlsoUnpairsThisPcThere()
    {
        using var a = StartPc("A");
        using var b = StartPc("B");
        await WaitUntil(() => a.Server.Status().Listening);

        Assert.Equal("A", await b.Client.JoinAsync(PairingLink.Build(a.Server.CreateOffer()!)));
        Assert.Single(a.Store.List(), d => d.Kind == MobileDeviceKind.Pc && d.Name == "B");

        await b.Client.Remove(a.Store.PcId);
        Assert.Empty(b.Client.Status());
        Assert.Empty(a.Store.List());

        // 다시 연결해도 A 에는 B 가 하나뿐이다.
        Assert.Equal("A", await b.Client.JoinAsync(PairingLink.Build(a.Server.CreateOffer()!)));
        Assert.Single(a.Store.List());

        // A 가 꺼져 있어도 B 목록에서는 지워지고 조용히 끝난다.
        a.Server.Dispose();
        await b.Client.Remove(a.Store.PcId);
        Assert.Empty(b.Client.Status());
        await b.Client.Remove("unknown-pc"); // 없는 PC 는 아무것도 하지 않는다
    }

    /// <summary>
    /// 한쪽이 설정에서 PC 이름을 바꾸면(#271) 다시 연결하지 않아도 다음 프레임부터 상대 PC 의 목록 이름이 따라간다 —
    /// 연결해 온 쪽(A 의 기기 목록)과 연결한 쪽(B 의 peers.json) 모두, 파일에도 남는다.
    /// </summary>
    [Fact]
    public async Task RenamedPcShowsUpUnderItsNewNameOnTheOtherSide()
    {
        var aName = "A";
        var bName = "B";
        var aDir = Path.Combine(_dir, "A");
        var bDir = Path.Combine(_dir, "B");
        var aStore = new MobileDeviceStore(aDir);
        var aHub = new PeerSyncHub();
        using var aServer = new MobileApiServer(new MobileUsageFeed(), aStore, new PairingService(aStore), () => aName, aHub);
        aServer.Start(new MobileServerOptions(FreePort(), Lan: false, Tailscale: false, BindLoopback: true));
        var bStore = new MobileDeviceStore(bDir);
        var bHub = new PeerSyncHub();
        var bPeers = new SyncPeerStore(bDir);
        using var bClient = new PeerSyncClient(bHub, bPeers, () => bStore.PcId, () => bName);
        await WaitUntil(() => aServer.Status().Listening);

        Assert.Equal("A", await bClient.JoinAsync(PairingLink.Build(aServer.CreateOffer()!)));
        await WaitUntil(() => bClient.Status().All(s => s.Connected));
        Assert.Equal("B", aStore.List().Single(d => d.Kind == MobileDeviceKind.Pc).Name);

        bName = "Study laptop";
        bHub.PublishLocal(Snapshot("dev-b", bName, "claude", "org", 0.2)); // 다음 전송을 일으킨다
        await WaitUntil(() => aStore.List().Single(d => d.Kind == MobileDeviceKind.Pc).Name == "Study laptop");
        Assert.Equal("Study laptop", new MobileDeviceStore(aDir).List().Single(d => d.Kind == MobileDeviceKind.Pc).Name);

        aName = "Office desktop";
        aHub.PublishLocal(Snapshot("dev-a", aName, "claude", "org", 0.1));
        await WaitUntil(() => bPeers.All().Single().PcName == "Office desktop");
        Assert.Equal("Office desktop", new SyncPeerStore(bDir).All().Single().PcName);
        Assert.Equal("Office desktop", bClient.Status().Single().Peer.PcName);
    }

    /// <summary>
    /// 연결받는 PC 가 서버를 멈추면(연동 끄기·포트 변경) 연결해 온 PC 도 곧바로 끊긴 것을 안다(#274).
    /// </summary>
    [Fact]
    public async Task StoppingTheHostServerDropsTheSyncConnectionRightAway()
    {
        var aDir = Path.Combine(_dir, "A");
        var bDir = Path.Combine(_dir, "B");
        var aStore = new MobileDeviceStore(aDir);
        using var aServer = new MobileApiServer(new MobileUsageFeed(), aStore, new PairingService(aStore), () => "A", new PeerSyncHub());
        aServer.Start(new MobileServerOptions(FreePort(), Lan: false, Tailscale: false, BindLoopback: true));
        var bStore = new MobileDeviceStore(bDir);
        using var bClient = new PeerSyncClient(new PeerSyncHub(), new SyncPeerStore(bDir), () => bStore.PcId, () => "B");
        await WaitUntil(() => aServer.Status().Listening);
        await bClient.JoinAsync(PairingLink.Build(aServer.CreateOffer()!));
        await WaitUntil(() => bClient.Status().All(s => s.Connected));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        aServer.Stop();
        await WaitUntil(() => bClient.Status().All(s => !s.Connected));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), $"sync connection stayed up {watch.Elapsed.TotalSeconds:F1}s after Stop()");
    }

    /// <summary>
    /// 표가 그대로여도 이름을 바꾸고 <see cref="PeerSyncHub.Touch"/> 를 부르면 두 방향 모두 곧바로 새 이름이 간다(#274).
    /// </summary>
    [Fact]
    public async Task TouchCarriesARenameAcrossWithoutANewSnapshot()
    {
        var aName = "A";
        var bName = "B";
        var aDir = Path.Combine(_dir, "A");
        var bDir = Path.Combine(_dir, "B");
        var aStore = new MobileDeviceStore(aDir);
        var aHub = new PeerSyncHub();
        using var aServer = new MobileApiServer(new MobileUsageFeed(), aStore, new PairingService(aStore), () => aName, aHub);
        aServer.Start(new MobileServerOptions(FreePort(), Lan: false, Tailscale: false, BindLoopback: true));
        var bStore = new MobileDeviceStore(bDir);
        var bHub = new PeerSyncHub();
        var bPeers = new SyncPeerStore(bDir);
        using var bClient = new PeerSyncClient(bHub, bPeers, () => bStore.PcId, () => bName);
        await WaitUntil(() => aServer.Status().Listening);
        await bClient.JoinAsync(PairingLink.Build(aServer.CreateOffer()!));
        await WaitUntil(() => bClient.Status().All(s => s.Connected));

        bName = "Study laptop";
        bHub.Touch();
        await WaitUntil(() => aStore.List().Single(d => d.Kind == MobileDeviceKind.Pc).Name == "Study laptop");

        aName = "Office desktop";
        aHub.Touch();
        await WaitUntil(() => bPeers.All().Single().PcName == "Office desktop");
    }

    [Fact]
    public void PeerAndDeviceRenamesIgnoreBlankAndUnchangedNames()
    {
        var dir = Path.Combine(_dir, "rename");
        var peers = new SyncPeerStore(dir);
        peers.Upsert(new SyncPeer("pc-1", "OLD", [], "dev", "token", DateTimeOffset.UtcNow));
        var changes = 0;
        peers.Changed += () => changes++;
        Assert.False(peers.UpdateName("pc-1", "  "));
        Assert.False(peers.UpdateName("pc-1", "OLD"));
        Assert.False(peers.UpdateName("missing", "NEW"));
        Assert.True(peers.UpdateName("pc-1", " Home\nPC "));
        Assert.Equal("Home PC", peers.All().Single().PcName);
        Assert.Equal(1, changes);

        var devices = new MobileDeviceStore(dir);
        var phone = devices.Add("Pixel", null, new string('a', 64));
        var pc = devices.Add("DESKTOP-1", "Windows PC", new string('b', 64), MobileDeviceKind.Pc);
        Assert.False(devices.RenamePc(phone.DeviceId, "Renamed phone")); // 휴대폰 이름은 페어링 때 정해진다
        Assert.False(devices.RenamePc(pc.DeviceId, ""));
        Assert.True(devices.RenamePc(pc.DeviceId, "Gaming PC"));
        Assert.Equal(["Pixel", "Gaming PC"], devices.List().Select(d => d.Name));
    }

    [Fact]
    public void LocalSnapshotWithoutQuotaKeepsTheLastGoodOne()
    {
        var hub = new PeerSyncHub();
        var good = Snapshot("me", "ME", "claude", "org", 0.4, DateTimeOffset.Now.AddMinutes(-3));
        hub.PublishLocal(good);
        var failed = Snapshot("me", "ME", "claude", "org", 0, DateTimeOffset.Now);
        failed.Quota = null; // API 실패·쿨다운
        hub.PublishLocal(failed);

        var kept = hub.Snapshot().Single();
        Assert.Equal(0.4, kept.Quota!.ShortUsagePercent);
        Assert.Equal(good.Quota!.ObservedAtUtc, kept.Quota.ObservedAtUtc); // 원래 관측 시각 유지

        // 다른 계정이면 이어 붙이지 않는다.
        var other = Snapshot("me", "ME", "claude", "org-2", 0, DateTimeOffset.Now);
        other.Quota = null;
        hub.PublishLocal(other);
        Assert.Null(hub.Snapshot().Single().Quota);
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
}
