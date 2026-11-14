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
        var server = new MobileApiServer(new MobileUsageFeed(), store, new PairingService(store), name, hub);
        server.Start(new MobileServerOptions(FreePort(), Lan: false, Tailscale: false, BindLoopback: true));
        var client = new PeerSyncClient(hub, new SyncPeerStore(dir), () => store.PcId, name);
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
