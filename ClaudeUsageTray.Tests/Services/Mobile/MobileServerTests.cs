using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Models;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// 페어링 규칙과 휴대폰 API(#220)를 루프백 소켓으로 끝까지 확인한다 — QR 코드 → 토큰 → REST → WebSocket → 해제.
/// </summary>
public class MobileServerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cut-mobile-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void PairingCodeIsSingleUseAndExpires()
    {
        var time = new FakeTime(MobileProtocolFixtures.Now);
        var store = new MobileDeviceStore(_dir, time);
        var pairing = new PairingService(store, time);

        var (code, _) = pairing.Begin();
        var first = pairing.TryPair(code, "Pixel", "Google Pixel 9");
        Assert.Equal(PairOutcome.Paired, first.Outcome);
        Assert.NotNull(store.Authenticate(first.Token!));
        Assert.Equal(PairOutcome.InvalidCode, pairing.TryPair(code, "Pixel", null).Outcome);

        var (late, _) = pairing.Begin();
        time.Now += PairingService.CodeLifetime + TimeSpan.FromSeconds(1);
        Assert.Equal(PairOutcome.Expired, pairing.TryPair(late, "Pixel", null).Outcome);
    }

    [Fact]
    public void RepeatedWrongCodesLockPairing()
    {
        var time = new FakeTime(MobileProtocolFixtures.Now);
        var pairing = new PairingService(new MobileDeviceStore(_dir, time), time);
        var (code, _) = pairing.Begin();
        for (var i = 0; i < PairingService.MaxFailures; i++)
            Assert.Equal(PairOutcome.InvalidCode, pairing.TryPair("wrong", null, null).Outcome);
        Assert.Equal(PairOutcome.Locked, pairing.TryPair(code, null, null).Outcome);

        // 잠금 창이 지나면 다시 받는다. 잠금 창과 코드 수명이 같아 옛 코드는 이미 만료됐으므로 새 QR 로 확인한다.
        time.Now += PairingService.FailureWindow + TimeSpan.FromSeconds(1);
        var (fresh, _) = pairing.Begin();
        Assert.Equal(PairOutcome.Paired, pairing.TryPair(fresh, null, null).Outcome);
    }

    [Fact]
    public void StoreKeepsOnlyTokenHashesAndSurvivesCorruptFile()
    {
        var store = new MobileDeviceStore(_dir);
        var pairing = new PairingService(store);
        var (code, _) = pairing.Begin();
        var token = pairing.TryPair(code, "Galaxy", null).Token!;
        var file = File.ReadAllText(Path.Combine(_dir, "state.json"));
        Assert.DoesNotContain(token, file);
        Assert.Contains(PairingService.HashToken(token), file);

        // 다시 읽어도 같은 PC·기기.
        var reloaded = new MobileDeviceStore(_dir);
        Assert.Equal(store.PcId, reloaded.PcId);
        Assert.NotNull(reloaded.Authenticate(token));

        // 깨진 파일은 보존하고 빈 상태로 시작한다(트레이를 멈추지 않는다).
        File.WriteAllText(Path.Combine(_dir, "state.json"), "{ not json");
        var fresh = new MobileDeviceStore(_dir);
        Assert.Empty(fresh.List());
        Assert.Matches("^[0-9a-f]{32}$", fresh.PcId);
        Assert.NotEmpty(Directory.GetFiles(_dir, "state.json.corrupt-*"));
    }

    [Fact]
    public void FeedHashesAccountKeysAndDropsOldEntries()
    {
        var time = new FakeTime(MobileProtocolFixtures.Now);
        var feed = new MobileUsageFeed(time);
        feed.Publish(new MobileFeedEntry("codex", "acct|user", "", time.Now, "", null, null));
        feed.Publish(new MobileFeedEntry("claude", "org|acct", "Me", time.Now, "", null, null));

        var current = feed.Current("pc-a");
        Assert.Equal(["claude", "codex"], current.Select(c => c.Provider));
        Assert.DoesNotContain(current, c => c.AccountHash.Contains("acct"));
        Assert.Equal(MobileAccountHash.For("claude", "org|acct", "pc-a").Hash, current[0].AccountHash);

        time.Now += MobileUsageFeed.MaxAge + TimeSpan.FromMinutes(1);
        Assert.Empty(feed.Current("pc-a"));
    }

    [Fact]
    public void FeedKeepsLastGoodQuotaWhenARefreshFails()
    {
        var time = new FakeTime(MobileProtocolFixtures.Now);
        var feed = new MobileUsageFeed(time);
        var good = new UsageSyncQuotaSnapshot { HasData = true, ObservedAtUtc = time.Now, ShortUsagePercent = 0.4 };
        feed.Publish(new MobileFeedEntry("claude", "org|acct", "Me", time.Now, "", good, null));

        // 다음 조회가 레이트 리밋으로 실패: 오류 종류는 새 값, 쿼터는 직전 성공값과 그 관측 시각.
        time.Now += TimeSpan.FromMinutes(5);
        feed.Publish(new MobileFeedEntry("claude", "org|acct", "Me", time.Now, "rate_limited", null, null));
        var kept = feed.Current("pc-a").Single();
        Assert.Equal("rate_limited", kept.ErrorKind);
        Assert.Equal(time.Now, kept.ObservedAt);
        Assert.Equal(0.4, kept.Quota!.ShortUsagePercent);
        Assert.Equal(good.ObservedAtUtc, kept.Quota.ObservedAtUtc);

        // 계정이 바뀌었으면 잇지 않는다.
        feed.Publish(new MobileFeedEntry("claude", "org|other", "Other", time.Now, "", null, null));
        Assert.Null(feed.Current("pc-a").Single().Quota);

        // 너무 오래된 쿼터도 잇지 않는다.
        feed.Publish(new MobileFeedEntry("claude", "org|acct", "Me", time.Now, "", good, null));
        time.Now = good.ObservedAtUtc!.Value + MobileUsageFeed.MaxAge + TimeSpan.FromMinutes(1);
        feed.Publish(new MobileFeedEntry("claude", "org|acct", "Me", time.Now, "api_failed", null, null));
        Assert.Null(feed.Current("pc-a").Single().Quota);
    }

    [Fact]
    public async Task PairThenReadUsageAndLiveThenRevoke()
    {
        var store = new MobileDeviceStore(_dir);
        var pairing = new PairingService(store);
        var feed = new MobileUsageFeed();
        using var server = new MobileApiServer(feed, store, pairing, "TEST-PC");
        var port = FreePort();
        server.Start(new MobileServerOptions(port, Lan: false, Tailscale: false, BindLoopback: true));
        await WaitUntil(() => server.Status().Listening);

        var offer = server.CreateOffer();
        Assert.NotNull(offer);
        Assert.Contains(offer!.Endpoints, e => e.Transport == MobileTransport.Loopback && e.Port == port);

        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

        var info = JsonDocument.Parse(await http.GetStringAsync("/api/v1/info")).RootElement;
        Assert.Equal("TEST-PC", info.GetProperty("pcName").GetString());

        var wrong = await http.PostAsync("/api/v1/pair", Json(new { code = "nope", deviceName = "x" }));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var paired = await http.PostAsync("/api/v1/pair", Json(new { code = offer.Code, deviceName = "Pixel", deviceModel = "P9" }));
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var pairBody = JsonDocument.Parse(await paired.Content.ReadAsStringAsync()).RootElement;
        var token = pairBody.GetProperty("token").GetString()!;
        Assert.Equal(store.PcId, pairBody.GetProperty("pcId").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/usage")).StatusCode);

        feed.Publish(new MobileFeedEntry("claude", "org|acct", "Me", DateTimeOffset.UtcNow, "",
            new UsageSyncQuotaSnapshot { HasData = true, ShortUsagePercent = 0.5 }, null));
        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/usage"))
        {
            req.Headers.Authorization = new("Bearer", token);
            var usage = JsonDocument.Parse(await (await http.SendAsync(req)).Content.ReadAsStringAsync()).RootElement;
            var claude = usage.GetProperty("providers")[0];
            Assert.Equal("claude", claude.GetProperty("provider").GetString());
            Assert.Equal(0.5, claude.GetProperty("quota").GetProperty("shortUsagePercent").GetDouble());
        }

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
        await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/api/v1/live"), CancellationToken.None);
        var first = await ReceiveText(ws);
        Assert.Contains("\"claude\"", first);

        // 사용량이 바뀌면 곧바로 다음 프레임이 온다.
        feed.Publish(new MobileFeedEntry("codex", "a|u", "", DateTimeOffset.UtcNow, "", null, null));
        var second = await ReceiveText(ws);
        Assert.Contains("\"codex\"", second);

        // 트레이에서 해제하면 다음 전송 전에 1008 revoked 로 끊긴다.
        var deviceId = pairBody.GetProperty("deviceId").GetString()!;
        Assert.True(store.Revoke(deviceId));
        feed.Publish(new MobileFeedEntry("codex", "a|u", "", DateTimeOffset.UtcNow, "", null, null));
        var buffer = new byte[4096];
        WebSocketReceiveResult result;
        using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            result = await ws.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, ws.CloseStatus);
        Assert.Equal(MobileApiServer.RevokedCloseReason, ws.CloseStatusDescription);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<string> ReceiveText(ClientWebSocket ws)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[64 * 1024];
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await ws.ReceiveAsync(buffer, cts.Token);
            ms.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(ms.ToArray());
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
