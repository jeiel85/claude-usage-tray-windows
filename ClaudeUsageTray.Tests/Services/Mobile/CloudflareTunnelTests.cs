using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// Tailscale 없이 밖에서 닿는 Cloudflare Tunnel 경로(#225). 실제 cloudflared 대신 Quick Tunnel 로그를 흉내 내는 가짜 커넥터로
/// 상태·경로·정리를 확인하고, 경로 갱신 푸시의 서명·알림 규칙을 확인한다.
/// </summary>
public class CloudflareTunnelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cut-cf-tests-{Guid.NewGuid():N}");

    public CloudflareTunnelTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Theory]
    [InlineData("2026-11-14T05:00:00Z INF |  https://bright-fox-quiet-river.trycloudflare.com                              |", "bright-fox-quiet-river.trycloudflare.com")]
    [InlineData("INF Your quick Tunnel has been created! Visit it at: https://A-b-1.TryCloudflare.com", "a-b-1.trycloudflare.com")]
    [InlineData("ERR failed to request quick Tunnel: Post \"https://api.trycloudflare.com/tunnel\": dial tcp: i/o timeout", null)]
    [InlineData("INF Registered tunnel connection connIndex=0", null)]
    public void QuickTunnelHostIsReadFromTheLogButNotTheApiHost(string line, string? expected) =>
        Assert.Equal(expected, CloudflareTunnel.ParseQuickTunnelHost(line));

    [Theory]
    [InlineData("https://PC.Example.com/", "pc.example.com")]
    [InlineData(" pc.example.com:443 ", "pc.example.com")]
    [InlineData("pc.example.com.", "pc.example.com")]
    [InlineData("", null)]
    [InlineData("bad host/x", null)]
    public void HostnameIsCleaned(string raw, string? expected) => Assert.Equal(expected, CloudflareTunnel.CleanHostname(raw));

    /// <summary>
    /// Quick Tunnel 주소 한 줄을 찍고 계속 살아 있는 가짜 커넥터. 실제 cloudflared 처럼 표준 오류로 로그를 쓴다.
    /// Windows CI 에서는 .cmd, 그 밖에서는 sh 스크립트.
    /// </summary>
    private string FakeConnector(string host)
    {
        if (OperatingSystem.IsWindows())
        {
            var path = Path.Combine(_dir, "cloudflared.cmd");
            File.WriteAllText(path, $"@echo off\r\necho INF ^|  https://{host}  ^| 1>&2\r\nping -n 60 127.0.0.1 >nul\r\n");
            return path;
        }
        else
        {
            var path = Path.Combine(_dir, "cloudflared");
            File.WriteAllText(path, $"#!/bin/sh\necho \"INF |  https://{host}  |\" >&2\nexec sleep 60\n");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return path;
        }
    }

    [Fact]
    public async Task QuickTunnelBecomesACloudflareEndpointAndStopsCleanly()
    {
        var exe = FakeConnector("calm-lake-sun-tree.trycloudflare.com");
        using var tunnel = new CloudflareTunnel(_ => exe);
        var changes = 0;
        tunnel.Changed += () => Interlocked.Increment(ref changes);

        tunnel.Start(new CloudflareTunnelOptions(47830));
        await WaitUntil(() => tunnel.Endpoint() is not null);

        Assert.Equal(new MobileEndpoint(MobileTransport.Cloudflare, "calm-lake-sun-tree.trycloudflare.com", 443, true), tunnel.Endpoint());
        Assert.Equal(CloudflareStatus.Quick, tunnel.Status()!.Mode);
        Assert.True(changes >= 2); // starting → running

        // 같은 설정으로 다시 Start 해도 다시 띄우지 않는다(Quick Tunnel 주소가 바뀌지 않게).
        tunnel.Start(new CloudflareTunnelOptions(47830));
        Assert.NotNull(tunnel.Endpoint());

        tunnel.Stop();
        Assert.Null(tunnel.Status());
        Assert.Null(tunnel.Endpoint());
    }

    [Fact]
    public async Task MissingConnectorIsReportedAsNotInstalled()
    {
        using var tunnel = new CloudflareTunnel(_ => null);
        tunnel.Start(new CloudflareTunnelOptions(47830));
        await WaitUntil(() => tunnel.Status()?.State == CloudflareStatus.NotInstalled);
        Assert.Null(tunnel.Endpoint());
    }

    [Fact]
    public async Task NamedTunnelNeedsAHostname()
    {
        using var tunnel = new CloudflareTunnel(_ => throw new InvalidOperationException("must not start"));
        tunnel.Start(new CloudflareTunnelOptions(47830, Token: "secret-token"));
        await WaitUntil(() => tunnel.Status()?.State == CloudflareStatus.Error);
        Assert.Equal("hostname_required", tunnel.Status()!.Detail);
        Assert.Equal(CloudflareStatus.Named, tunnel.Status()!.Mode);
    }

    [Fact]
    public async Task ServerAdvertisesTheTunnelButNotItsLoopbackOrigin()
    {
        var dir = Path.Combine(_dir, "pc");
        var store = new MobileDeviceStore(dir);
        using var server = new MobileApiServer(new MobileUsageFeed(), store, new PairingService(store), "PC");
        MobileEndpoint? tunnel = null;
        server.TunnelEndpoint = () => tunnel;
        server.Start(new MobileServerOptions(FreePort(), Lan: false, Tailscale: false, BindLoopback: false, Cloudflare: true));
        await WaitUntil(() => server.Status().Listening);

        // 터널이 아직 열리지 않았으면 알릴 경로가 없다 — 닿지 않는 QR 을 만들지 않는다.
        Assert.Empty(server.Endpoints());
        Assert.Null(server.CreateOffer());

        tunnel = new MobileEndpoint(MobileTransport.Cloudflare, "calm-lake-sun-tree.trycloudflare.com", 443, true);
        var endpoint = Assert.Single(server.Endpoints());
        Assert.Equal(MobileTransport.Cloudflare, endpoint.Transport);
        Assert.Equal(endpoint, Assert.Single(server.CreateOffer()!.Endpoints));

        // 커넥터가 닿는 루프백 리스너는 실제로 열려 있다(평문 HTTP).
        using var http = new System.Net.Http.HttpClient();
        var info = await http.GetStringAsync($"http://127.0.0.1:{server.Options.Port}/api/v1/info");
        Assert.Contains("\"pcName\":\"PC\"", info);
    }

    [Fact]
    public void EndpointsPushMatchesFixtureAndVerifiesWithTheDeviceToken()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MobileProtocolFixtures.DeviceToken))).ToLowerInvariant();
        var bytes = PushEndpoints.Serialize(MobileProtocolFixtures.PcId, MobileProtocolFixtures.Endpoints(), MobileProtocolFixtures.Now, hash);
        var pretty = new JsonSerializerOptions(MobileProtocol.Json) { WriteIndented = true };
        var envelope = JsonSerializer.Deserialize<PushEndpointsEnvelope>(bytes, MobileProtocol.Json)!;
        MobileProtocolGoldenTests.AssertGoldenFile("push-endpoints.v1.json", JsonSerializer.Serialize(envelope, pretty));

        Assert.Equal(PushEndpoints.Kind, envelope.Kind);
        var data = Base64Url.Decode(envelope.Data);
        // 휴대폰이 하는 검사: 토큰 → SHA-256 원시 32바이트를 키로 HMAC.
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(MobileProtocolFixtures.DeviceToken));
        Assert.Equal(HMACSHA256.HashData(key, data), Base64Url.Decode(envelope.Mac));
        var inner = JsonSerializer.Deserialize<PushEndpointsData>(data, MobileProtocol.Json)!;
        Assert.Equal(MobileProtocolFixtures.PcId, inner.PcId);
        Assert.Equal(MobileProtocolFixtures.Endpoints(), inner.Endpoints);
        Assert.True(bytes.Length <= WebPushEncryption.MaxPlaintextBytes);
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task AnnouncerSendsNewPathsOncePerPhoneAndRetriesOnlyMissedOnes()
    {
        var time = new FakeTime(MobileProtocolFixtures.Now);
        var lan = new MobileEndpoint(MobileTransport.Lan, "192.168.0.10", 47830, true, MobileProtocolFixtures.Pin);
        var cf1 = new MobileEndpoint(MobileTransport.Cloudflare, "one.trycloudflare.com", 443, true);
        var cf2 = new MobileEndpoint(MobileTransport.Cloudflare, "two.trycloudflare.com", 443, true);
        IReadOnlyList<MobileEndpoint> endpoints = [lan];
        IReadOnlyList<string> targets = ["phone-a", "phone-b"];
        var accepting = new HashSet<string> { "phone-a", "phone-b" };
        var sent = new List<(string Device, int Count)>();
        using var announcer = new EndpointAnnouncer(() => endpoints, () => targets, (list, ids, _) =>
        {
            foreach (var id in ids) sent.Add((id, list.Count));
            return Task.FromResult<IReadOnlyList<string>>(ids.Where(accepting.Contains).ToList());
        }, time);

        Assert.False(await announcer.TickAsync()); // 첫 관찰: 아직 안정되지 않음
        Assert.True(await announcer.TickAsync());  // 두 번 같으면 두 휴대폰에 보낸다
        Assert.Equal(2, sent.Count);
        Assert.False(await announcer.TickAsync()); // 이미 알린 목록

        // 터널이 열려 새 경로가 생겼지만 phone-b 가 받지 못했다.
        endpoints = [lan, cf1];
        accepting.Remove("phone-b");
        Assert.False(await announcer.TickAsync());
        Assert.True(await announcer.TickAsync());
        Assert.Equal(4, sent.Count);

        // 재시도 간격 전에는 보내지 않고, 지나면 받지 못한 phone-b 에만 다시 보낸다.
        accepting.Add("phone-b");
        Assert.False(await announcer.TickAsync());
        time.Now += EndpointAnnouncer.RetryAfter;
        Assert.True(await announcer.TickAsync());
        Assert.Equal(("phone-b", 2), sent[^1]);
        Assert.Equal(5, sent.Count);

        // 경로가 빠지기만 하면 보내지 않는다(Wi-Fi 끊김).
        endpoints = [cf1];
        await announcer.TickAsync();
        Assert.False(await announcer.TickAsync());

        // 앱을 다시 시작해 Quick Tunnel 주소가 바뀌면 다시 알린다.
        endpoints = [lan, cf2];
        await announcer.TickAsync();
        Assert.True(await announcer.TickAsync());
        Assert.Equal(7, sent.Count);

        // 나중에 푸시를 등록한 휴대폰도 한 번 받는다.
        targets = ["phone-a", "phone-b", "phone-c"];
        accepting.Add("phone-c");
        Assert.True(await announcer.TickAsync());
        Assert.Equal(("phone-c", 2), sent[^1]);
    }

    [Fact]
    public void PeerSyncTriesCloudflareAfterLanAndTailscale()
    {
        var order = PeerSyncClient.Order(
        [
            new(MobileTransport.Cloudflare, "x.trycloudflare.com", 443, true),
            new(MobileTransport.Tailscale, "100.101.102.103", 47830, false),
            new(MobileTransport.Lan, "192.168.0.10", 47830, true, MobileProtocolFixtures.Pin),
        ]).Select(e => e.Transport).ToList();
        Assert.Equal([MobileTransport.Lan, MobileTransport.Tailscale, MobileTransport.Cloudflare], order);
        Assert.Equal("https://x.trycloudflare.com:443/api/v1/sync",
            PeerSyncClient.Url(new(MobileTransport.Cloudflare, "x.trycloudflare.com", 443, true), "/api/v1/sync"));
    }

    [Fact]
    public void SecretRoundTrips()
    {
        var stored = MobileSecret.Protect("tunnel-token");
        Assert.DoesNotContain("tunnel-token", OperatingSystem.IsWindows() ? stored : "");
        Assert.Equal("tunnel-token", MobileSecret.Unprotect(stored));
        Assert.Null(MobileSecret.Unprotect(""));
        Assert.Null(MobileSecret.Unprotect("not base64 at all"));
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
