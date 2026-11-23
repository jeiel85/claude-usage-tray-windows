using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 보안 감사 취약점 수정(#340) 회귀 테스트.
/// </summary>
public class SecurityHardeningTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cut-sec-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // 1. PowerShell 유니코드 곡선 따옴표(U+2018~U+201B) 이스케이프
    [Fact]
    public void MobileFirewall_PsQuote_EscapesAsciiAndUnicodeCurlyQuotes()
    {
        var raw = "C:\\Users\\O'Brien \u2018a\u2019b\u201Ac\u201Bd\\app.exe";
        var quoted = MobileFirewall.PsQuote(raw);
        Assert.Equal("'C:\\Users\\O''Brien \u2018\u2018a\u2019\u2019b\u201A\u201Ac\u201B\u201Bd\\app.exe'", quoted);
    }

    [Fact]
    public void UpdateService_BuildSwapScript_EscapesUnicodeCurlyQuotes()
    {
        var dir = "C:\\Users\\O\u2019Brien; calc; #";
        var script = UpdateService.BuildSwapScript(
            Path.Combine(dir, "App.exe"),
            Path.Combine(dir, "App_new.exe"),
            Path.Combine(dir, "update.log"),
            Path.Combine(dir, "swap.ps1"));

        Assert.Contains("O\u2019\u2019Brien; calc; #", script);
        Assert.DoesNotContain("O\u2019Brien", script);
    }

    // 2. Tailscale 인터페이스 판별 — 비-Tailscale CGNAT 어댑터 제외
    [Theory]
    [InlineData("Tailscale", "Tailscale Tunnel", true)]
    [InlineData("tailscale0", "Wintun Userspace Tunnel", true)]
    [InlineData("Ethernet 2", "Tailscale Tunnel", true)]
    [InlineData("Wi-Fi", "Intel(R) Wi-Fi 6E AX211", false)]
    [InlineData("Ethernet", "Realtek PCIe GbE Family Controller", false)]
    [InlineData("Cellular", "Mobile Broadband Adapter", false)]
    [InlineData(null, null, false)]
    public void MobileNetwork_IsTailscaleInterface_MatchesOnlyTailscaleAdapters(string? name, string? desc, bool expected)
    {
        Assert.Equal(expected, MobileNetwork.IsTailscaleInterface(name, desc));
    }

    // 3. 푸시 구독 SSRF 방어 — 비공인 IP 및 로컬 호스트 차단
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("172.16.5.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd12:3456:789a::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("::ffff:192.168.0.1", false)]
    public void MobileNetwork_IsPublicAddress_BlocksNonPublicRanges(string ipText, bool expected)
    {
        Assert.Equal(expected, MobileNetwork.IsPublicAddress(IPAddress.Parse(ipText)));
    }

    [Theory]
    [InlineData("https://127.0.0.1/push")]
    [InlineData("https://localhost/push")]
    [InlineData("https://sub.localhost/push")]
    [InlineData("https://printer.local/push")]
    [InlineData("https://metadata.internal/push")]
    [InlineData("https://192.168.0.10/push")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://[::1]/push")]
    public void PushPayload_Validate_RejectsLocalAndPrivateEndpoints(string endpoint)
    {
        var req = Services.Mobile.MobilePushTests.SampleSubscription() with { Endpoint = endpoint };
        Assert.NotNull(PushPayload.Validate(req));
    }

    [Fact]
    public async Task PushHttpClient_BlocksLoopbackConnectionAtConnectCallback()
    {
        using var client = MobileLinkService.CreatePushHttpClient();
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.GetAsync("http://127.0.0.1:1/"));
        Assert.Contains("non-public", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // 4. 시스템 바이너리 절대 경로 고정
    [Fact]
    public void MobileFirewall_SystemBinaryPath_ResolvesUnderSystemDirectory()
    {
        if (!OperatingSystem.IsWindows()) return;
        var ps = MobileFirewall.SystemBinaryPath(@"WindowsPowerShell\v1.0\powershell.exe");
        var netsh = MobileFirewall.SystemBinaryPath("netsh.exe");
        Assert.True(Path.IsPathFullyQualified(ps));
        Assert.StartsWith(Environment.SystemDirectory, ps, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(Environment.SystemDirectory, netsh, StringComparison.OrdinalIgnoreCase);
    }

    // 5. /api/v1/sync 기기 종류(Kind) 인가 및 PeerSyncHub 상한
    [Fact]
    public async Task MobileApiServer_SyncEndpoint_RejectsPhoneTokenWith403()
    {
        var store = new MobileDeviceStore(_dir);
        var pairing = new PairingService(store);
        var hub = new PeerSyncHub();
        using var server = new MobileApiServer(new MobileUsageFeed(), store, pairing, () => "TEST-PC", hub);
        var port = FreePort();
        server.Start(new MobileServerOptions(port, Lan: false, Tailscale: false, BindLoopback: true));
        await WaitUntil(() => server.Status().Listening);

        var phoneToken = pairing.TryPair(pairing.Begin().Code, "Pixel", "P9", MobileDeviceKind.Phone).Token!;

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/sync");
        req.Headers.Authorization = new("Bearer", phoneToken);
        var res = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public void PeerSyncHub_EnforcesEntryFrameAndIdentifierBounds()
    {
        var now = DateTimeOffset.UtcNow;
        var hub = new PeerSyncHub(() => now);

        // 과도한 길이의 식별자는 거부한다.
        var longId = MakeSnapshot(new string('d', PeerSyncHub.MaxIdLength + 1), "claude", now);
        Assert.False(hub.Merge([longId]));
        Assert.Empty(hub.Snapshot());

        // 프레임당 상한(MaxSnapshotsPerFrame) 및 테이블 상한(MaxEntries)을 넘기지 않는다.
        var hugeFrame = Enumerable.Range(0, PeerSyncHub.MaxSnapshotsPerFrame + 50)
            .Select(i => MakeSnapshot($"dev-{i}", "claude", now))
            .ToList();
        Assert.True(hub.Merge(hugeFrame));
        Assert.Equal(PeerSyncHub.MaxSnapshotsPerFrame, hub.Snapshot().Count);

        for (var batch = 1; batch <= 4; batch++)
        {
            var more = Enumerable.Range(batch * 100, 100)
                .Select(i => MakeSnapshot($"dev-{i}", "claude", now))
                .ToList();
            hub.Merge(more);
        }
        Assert.True(hub.Snapshot().Count <= PeerSyncHub.MaxEntries);
    }

    // 6. PeerSyncClient 엔드포인트 검증 및 WebSocket URL 치환
    [Theory]
    [InlineData("lan", "192.168.0.10", 47830, true, "4f9a8a7d1c2b3e4f5a6b7c8d9e0f11223344556677889900aabbccddeeff0011", true, true)]
    [InlineData("lan", "192.168.0.10", 47830, false, "4f9a8a7d1c2b3e4f5a6b7c8d9e0f11223344556677889900aabbccddeeff0011", true, false)] // 평문 LAN
    [InlineData("lan", "192.168.0.10", 47830, true, null, true, false)] // 핀 누락
    [InlineData("lan", "8.8.8.8", 47830, true, "4f9a8a7d1c2b3e4f5a6b7c8d9e0f11223344556677889900aabbccddeeff0011", true, false)] // 공인 IP LAN
    [InlineData("cloudflare", "pc.trycloudflare.com", 443, true, null, true, true)]
    [InlineData("cloudflare", "pc.trycloudflare.com", 443, false, null, true, false)] // 평문 Cloudflare
    [InlineData("cloudflare", "pc.trycloudflare.com", 80, true, null, true, false)] // 비-443
    [InlineData("cloudflare", "evil.com/path#", 443, true, null, true, false)] // URL 인젝션
    [InlineData("cloudflare", "user@evil.com", 443, true, null, true, false)] // @ 인젝션
    [InlineData("tailscale", "100.101.102.103", 47830, false, null, true, true)]
    [InlineData("tailscale", "home-pc.tail1234.ts.net", 47830, false, null, true, true)]
    [InlineData("tailscale", "evil.example.com", 47830, false, null, true, false)] // 비-Tailscale 평문 호스트
    [InlineData("loopback", "127.0.0.1", 47830, false, null, true, true)]
    [InlineData("loopback", "127.0.0.1", 47830, false, null, false, false)] // 원격 갱신 시 루프백 차단
    [InlineData("custom", "evil.com", 80, false, null, true, false)] // 미지원 전송
    public void PeerSyncClient_IsValidEndpoint_EnforcesTransportInvariants(
        string transport, string host, int port, bool tls, string? pin, bool allowLoopback, bool expected)
    {
        var ep = new MobileEndpoint(transport, host, port, tls, pin);
        Assert.Equal(expected, PeerSyncClient.IsValidEndpoint(ep, allowLoopback));
    }

    [Fact]
    public void PeerSyncClient_Url_OnlyReplacesSchemeForWebSocket()
    {
        var ep = new MobileEndpoint(MobileTransport.Cloudflare, "http-node.example.com", 443, true);
        Assert.Equal("wss://http-node.example.com:443/api/v1/sync", PeerSyncClient.Url(ep, "/api/v1/sync", webSocket: true));
    }

    // 7. MobileApiServer 브라우저 CSRF (Origin / Content-Type) 차단
    [Fact]
    public async Task MobileApiServer_RejectsCrossOriginAndNonJsonPostRequests()
    {
        var store = new MobileDeviceStore(_dir);
        var pairing = new PairingService(store);
        using var server = new MobileApiServer(new MobileUsageFeed(), store, pairing, () => "TEST-PC");
        var port = FreePort();
        server.Start(new MobileServerOptions(port, Lan: false, Tailscale: false, BindLoopback: true));
        await WaitUntil(() => server.Status().Listening);

        var offer = server.CreateOffer()!;
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        // Origin 헤더가 있는 브라우저 교차 출처 요청은 403 으로 차단되고 페어링 실패 횟수도 오르지 않는다.
        for (var i = 0; i < PairingService.MaxFailures + 2; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pair")
            {
                Content = new StringContent("{\"code\":\"wrong\"}", Encoding.UTF8, "application/json"),
            };
            req.Headers.Add("Origin", "https://evil.example");
            var res = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        }

        // Content-Type: text/plain 단순 요청은 415 로 차단되고 페어링 실패 횟수도 오르지 않는다.
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/pair")
        {
            Content = new StringContent("{\"code\":\"wrong\"}", Encoding.UTF8, "text/plain"),
        })
        {
            var res = await http.SendAsync(req);
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, res.StatusCode);
        }

        // 정상 네이티브 클라이언트는 여전히 원래 코드로 페어링할 수 있어야 한다.
        var ok = await http.PostAsync("/api/v1/pair",
            new StringContent(JsonSerializer.Serialize(new { code = offer.Code, deviceName = "Pixel" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    // 8. MobileDeviceStore — 같은 Endpoint 라도 P256dh 공개키가 다르면 타 기기를 철회하지 않는다
    [Fact]
    public void MobileDeviceStore_DoesNotRevokeOtherDeviceWithDifferentP256dh()
    {
        var store = new MobileDeviceStore(_dir);
        var d1 = store.Add("Phone1", null, new string('a', 64));
        var d2 = store.Add("Phone2", null, new string('b', 64));
        var endpoint = "https://ntfy.sh/upShared1234567890?up=1";

        store.SetPushSubscription(d1.DeviceId, new PushSubscription(endpoint, Services.Mobile.MobilePushTests.UaPublic, Services.Mobile.MobilePushTests.AuthSecret, DateTimeOffset.UtcNow));
        store.SetPushSubscription(d2.DeviceId, new PushSubscription(endpoint, Services.Mobile.MobilePushTests.AsPublic, Services.Mobile.MobilePushTests.AuthSecret, DateTimeOffset.UtcNow));

        Assert.True(store.IsPaired(d1.DeviceId));
        Assert.True(store.IsPaired(d2.DeviceId));
    }

    // 10. 방어 심층화: SanitizePathPart 및 UpdateService
    [Theory]
    [InlineData("..", "_")]
    [InlineData(".", "_")]
    [InlineData("a..b", "_")]
    [InlineData("claude", "claude")]
    [InlineData("2026-11-23", "2026-11-23")]
    public void UsageSyncService_SanitizePathPart_BlocksDotTraversal(string input, string expected)
    {
        Assert.Equal(expected, UsageSyncService.SanitizePathPart(input));
    }

    [Fact]
    public void UpdateService_SelectUpdate_RejectsNonHttpsAssetUrls()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = "v9.0.0",
                draft = false,
                prerelease = false,
                body = "notes",
                assets = new[]
                {
                    new { name = "ClaudeUsageTray.exe", browser_download_url = "http://insecure.example/ClaudeUsageTray.exe" },
                    new { name = UpdateSignature.AssetName, browser_download_url = "https://secure.example/ClaudeUsageTray.exe.sig" },
                },
            },
        });
        using var doc = JsonDocument.Parse(json);
        Assert.Null(UpdateService.SelectUpdate(doc.RootElement, new Version(1, 0, 0)));
    }

    private static UsageSyncSnapshot MakeSnapshot(string deviceId, string provider, DateTimeOffset now) => new()
    {
        SchemaVersion = UsageSyncSchema.CurrentVersion,
        AccountHash = UsageSyncService.BuildAccountHash(provider, "org"),
        Provider = provider,
        DeviceId = deviceId,
        DeviceName = deviceId,
        LocalDate = DateOnly.FromDateTime(now.LocalDateTime).ToString("yyyy-MM-dd"),
        ObservedAtUtc = now.ToUniversalTime(),
        LocalTotals = new UsageSyncLocalTotals(),
    };

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
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
}
