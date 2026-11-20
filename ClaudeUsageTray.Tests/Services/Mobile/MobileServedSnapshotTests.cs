using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.Services.Mobile;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// PC→휴대폰 실제 경로(#320): 공급자 쪽 쿼터 생성 → <see cref="MobileUsageFeed"/> → 페어링한 서버의 <c>/api/v1/live</c>
/// 첫 프레임을 그대로 받아 <c>docs/mobile-protocol/usage-snapshot.served.v1.json</c> 과 비교한다. 휴대폰의
/// <c>ServedSnapshotTest</c> 가 같은 파일을 앱 파서·계정 묶기로 읽는다 — 손으로 만든 픽스처(<see cref="MobileProtocolFixtures"/>)와
/// 달리, 새 공급자를 피드에 실었을 때 서버가 실제로 내보내는 모양이 앱에서 카드가 되는지까지 본다.
/// 보낼 때마다 바뀌는 값(<c>ts</c>·<c>pcId</c>·<c>appVersion</c>)만 고정 값으로 바꿔 비교한다. 그래서 계정은 계정 범위(해시가
/// pcId 에 기대지 않음)만 싣는다. 의도한 형식 변경이면 <c>CUT_UPDATE_GOLDEN=1</c> 로 다시 쓴다.
/// </summary>
public class MobileServedSnapshotTests : IDisposable
{
    private static readonly DateTimeOffset Now = MobileProtocolFixtures.Now;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cut-served-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task LiveFrame_MatchesTheFixtureThePhoneParses()
    {
        var store = new MobileDeviceStore(_dir);
        var pairing = new PairingService(store);
        var feed = new MobileUsageFeed(new FixedTime(Now));
        using var server = new MobileApiServer(feed, store, pairing, () => "HOME-PC");
        var port = FreePort();
        server.Start(new MobileServerOptions(port, Lan: false, Tailscale: false, BindLoopback: true));
        await WaitUntil(() => server.Status().Listening);

        // 실제 공급자 경로가 만드는 쿼터를 그대로 싣는다 — DeepSeek 은 HmsViewModel.CreateMobileQuota(#318).
        feed.Publish(new MobileFeedEntry("claude", "org-uuid-1|account-uuid-1", "김민준 · Acme Corp", Now.AddSeconds(-30), "",
            new UsageSyncQuotaSnapshot
            {
                HasData = true,
                ShortUsagePercent = 0.42,
                ShortResetAt = Now.AddHours(2),
                LongUsagePercent = 0.18,
                LongResetAt = Now.AddDays(3),
                PlanType = "max",
            },
            new MobileTodayTotals("2026-11-14", 120_000, 34_000, 2_500_000, 80_000, 3, 41)));
        var deepSeek = new HmsReadResult(HmsReadState.Ok,
            new HmsDailyUsage(100_000_000, 400_000_000, 300_000_000, 10, new DateTimeOffset(2026, 11, 15, 0, 0, 0, TimeSpan.Zero), null),
            HmsUsageService.KeyFingerprint("hms_example_key"));
        feed.Publish(new MobileFeedEntry(HmsUsageService.MobileProvider, deepSeek.KeyId, "", Now,
            MainViewModel.HmsMobileErrorKind(deepSeek.State), HmsViewModel.CreateMobileQuota(deepSeek, Now), null));

        var offer = server.CreateOffer()!;
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        var paired = await http.PostAsync("/api/v1/pair",
            new StringContent(JsonSerializer.Serialize(new { code = offer.Code, deviceName = "Pixel" }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var token = JsonDocument.Parse(await paired.Content.ReadAsStringAsync()).RootElement.GetProperty("token").GetString()!;

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/api/v1/live"), CancellationToken.None);
        var frame = JsonNode.Parse(await ReceiveText(ws))!.AsObject();

        Assert.Equal(store.PcId, (string?)frame["pcId"]);
        Assert.Equal(MobileApiServer.AppVersion, (string?)frame["appVersion"]);
        frame["ts"] = Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        frame["pcId"] = MobileProtocolFixtures.PcId;
        frame["appVersion"] = "1.72.6";

        MobileProtocolGoldenTests.AssertGoldenFile("usage-snapshot.served.v1.json",
            frame.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                // 읽기 쉬운 픽스처를 위해 한글·"+" 를 그대로 둔다(앱은 어느 쪽이든 같게 읽는다).
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static async Task<string> ReceiveText(ClientWebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        using var text = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, cts.Token);
            text.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return Encoding.UTF8.GetString(text.ToArray());
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
        for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(50);
        Assert.True(condition());
    }
}
