using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// 휴대폰 앱 푸시(#222): RFC 8291 암호화, 구독 저장, 전송 결과, 그리고 PC 와 앱이 같은 형식을 쓰는지(픽스처).
/// RFC 8291 부록 A 의 공개 테스트 벡터를 쓴다 — 결정적이라 Kotlin 테스트가 같은 값으로 복호화해 두 구현을 맞춰 본다.
/// </summary>
public class MobilePushTests : IDisposable
{
    internal const string AsPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    internal const string AsPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    internal const string UaPublic = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    internal const string UaPrivate = "q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94";
    internal const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    internal const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string Header = "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string Ciphertext = "8pfeW0KbunFT06SuDKoJH9Ql87S1QUrdirN6GcG7sFz1y1sqLgVi1VhjVkHsUoEsbI_0LpXMuGvnzQ";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cut-push-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    internal static ECDiffieHellman Key(string privateB64, string publicB64)
    {
        var pub = Base64Url.Decode(publicB64);
        var key = ECDiffieHellman.Create();
        key.ImportParameters(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Base64Url.Decode(privateB64),
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
        return key;
    }

    [Fact]
    public void EncryptReproducesRfc8291AppendixA()
    {
        using var asKey = Key(AsPrivate, AsPublic);
        var body = WebPushEncryption.Encrypt(
            Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"),
            Base64Url.Decode(UaPublic), Base64Url.Decode(AuthSecret), asKey, Base64Url.Decode(Salt));
        Assert.Equal(Base64Url.Decode(Header), body[..86]);
        Assert.Equal(Base64Url.Decode(Ciphertext), body[86..]);
    }

    [Fact]
    public void TamperedOrWrongKeyFailsTheTag()
    {
        using var uaKey = Key(UaPrivate, UaPublic);
        var body = Base64Url.Decode(Header).Concat(Base64Url.Decode(Ciphertext)).ToArray();
        Assert.Equal("When I grow up, I want to be a watermelon",
            Encoding.UTF8.GetString(WebPushEncryption.Decrypt(body, uaKey, Base64Url.Decode(AuthSecret))));
        var tampered = (byte[])body.Clone();
        tampered[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => WebPushEncryption.Decrypt(tampered, uaKey, Base64Url.Decode(AuthSecret)));
        Assert.ThrowsAny<CryptographicException>(() => WebPushEncryption.Decrypt(body, uaKey, new byte[16]));
    }

    public static PushSubscriptionRequest SampleSubscription() => new(MobileProtocol.Schema,
        "https://ntfy.sh/upK3x9QeZt0b2Wm7?up=1", UaPublic, AuthSecret, ["weather"]);

    public static PushMessage SampleMessage() => new(MobileProtocol.Schema,
        "5b0e1c2d3f4a49b8a1c2d3e4f5a6b7c8", MobileProtocolFixtures.PcId, "HOME-PC", PushCategories.Usage, PushPriority.Warning,
        "Claude 90% · 5시간 한도", "15:30 초기화 · 1h 12m 남음", "claude-usage-short-90-a1b2c3_1794620400", 300, null,
        MobileProtocolFixtures.Now, "claude",
        MobileAccountHash.For("claude", "org-uuid|account-uuid", MobileProtocolFixtures.PcId).Hash, "김민준 · Acme Corp");

    [Fact]
    public void PushFixturesMatch()
    {
        var pretty = new JsonSerializerOptions(MobileProtocol.Json) { WriteIndented = true };
        MobileProtocolGoldenTests.AssertGoldenFile("push-subscription.v1.json", JsonSerializer.Serialize(SampleSubscription(), pretty));
        MobileProtocolGoldenTests.AssertGoldenFile("push-message.v1.json", JsonSerializer.Serialize(SampleMessage(), pretty));

        // 앱이 같은 부록 A 휴대폰 키로 이 파일을 복호화해 push-message.v1.json 과 같은지 확인한다.
        using var asKey = Key(AsPrivate, AsPublic);
        var body = WebPushEncryption.Encrypt(PushPayload.Serialize(SampleMessage()),
            Base64Url.Decode(UaPublic), Base64Url.Decode(AuthSecret), asKey, Base64Url.Decode(Salt));
        MobileProtocolGoldenTests.AssertGoldenFile("push-message.v1.aes128gcm.txt", Base64Url.Encode(body));
    }

    [Fact]
    public void ValidationRejectsHttpUserInfoAndOffCurveKeys()
    {
        Assert.Null(PushPayload.Validate(SampleSubscription()));
        Assert.NotNull(PushPayload.Validate(SampleSubscription() with { Endpoint = "http://ntfy.sh/x" }));
        Assert.NotNull(PushPayload.Validate(SampleSubscription() with { Endpoint = "https://u:p@ntfy.sh/x" }));
        var key = Base64Url.Decode(UaPublic);
        key[^1] ^= 1;
        Assert.NotNull(PushPayload.Validate(SampleSubscription() with { P256dh = Base64Url.Encode(key) }));
        Assert.NotNull(PushPayload.Validate(SampleSubscription() with { MutedCategories = ["Bad Category"] }));
    }

    [Fact]
    public void LongBodiesAreTrimmedToFitOnePush()
    {
        var huge = SampleMessage() with { Body = new string('가', 5000) };
        var bytes = PushPayload.Serialize(huge);
        Assert.True(bytes.Length <= WebPushEncryption.MaxPlaintextBytes);
        Assert.EndsWith("…", JsonDocument.Parse(bytes).RootElement.GetProperty("body").GetString());
    }

    [Fact]
    public void StoreKeepsOneSubscriptionPerPhoneAndRevokesTheOldPairing()
    {
        var store = new MobileDeviceStore(_dir);
        var oldPairing = store.Add("Pixel", null, new string('a', 64));
        var newPairing = store.Add("Pixel", null, new string('b', 64));
        var sub = new PushSubscription(SampleSubscription().Endpoint, UaPublic, AuthSecret, DateTimeOffset.UtcNow);
        Assert.True(store.SetPushSubscription(oldPairing.DeviceId, sub));
        Assert.True(store.SetPushSubscription(newPairing.DeviceId, sub));

        // 같은 엔드포인트 = 같은 휴대폰의 예전 페어링 → 철회(알림이 두 번 가지 않게).
        Assert.False(store.IsPaired(oldPairing.DeviceId));
        Assert.Single(store.PushTargets());
        Assert.True(store.List().Single().HasPush);

        store.SetPushSubscription(newPairing.DeviceId, null);
        Assert.Empty(store.PushTargets());
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpStatusCode> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(respond(request)));
        }
    }

    [Fact]
    public async Task SenderEncryptsSkipsMutedAndDropsGoneEndpoints()
    {
        var store = new MobileDeviceStore(_dir);
        var phone = store.Add("Pixel", null, new string('a', 64));
        var sender = new AppPushSender(store, new HttpClient(new FakeHandler(_ => HttpStatusCode.Created)), "HOME-PC");
        Assert.Equal(AppPushResult.NoTargets, await sender.SendAsync(SampleMessage()));

        store.SetPushSubscription(phone.DeviceId, new PushSubscription(
            SampleSubscription().Endpoint, UaPublic, AuthSecret, DateTimeOffset.UtcNow, ["weather"]));
        var handler = new FakeHandler(_ => HttpStatusCode.Created);
        sender = new AppPushSender(store, new HttpClient(handler), "HOME-PC");

        Assert.Equal(AppPushResult.Delivered, await sender.SendAsync(SampleMessage()));
        var request = handler.Requests.Single();
        Assert.Equal("aes128gcm", request.Content!.Headers.ContentEncoding.Single());
        Assert.Equal("high", request.Headers.GetValues("Urgency").Single());

        // 휴대폰이 끈 범주는 보내지 않고, 토픽으로도 넘기지 않도록 AllMuted 로 알린다.
        Assert.Equal(AppPushResult.AllMuted, await sender.SendAsync(SampleMessage() with { Category = PushCategories.Weather }));
        Assert.Single(handler.Requests);

        // 410: 엔드포인트가 사라졌으므로 구독을 지운다.
        sender = new AppPushSender(store, new HttpClient(new FakeHandler(_ => HttpStatusCode.Gone)), "HOME-PC");
        Assert.Equal(AppPushResult.Failed, await sender.SendAsync(SampleMessage()));
        Assert.Empty(store.PushTargets());
    }

    [Fact]
    public void BuildCarriesTheSameAccountHashAsTheUsageFeed()
    {
        var store = new MobileDeviceStore(_dir);
        var sender = new AppPushSender(store, new HttpClient(new FakeHandler(_ => HttpStatusCode.Created)), "HOME-PC");

        var claude = sender.Build(PushCategories.Usage, "t", "b", 4, null, TimeSpan.Zero, null,
            new PushAccount("Claude", "org|acct", " 김민준 · Acme "));
        Assert.Equal("claude", claude.Provider);
        Assert.Equal(MobileAccountHash.For("claude", "org|acct", store.PcId).Hash, claude.AccountHash);
        Assert.Equal("김민준 · Acme", claude.AccountLabel);

        // 계정 키를 모르면 피드처럼 이 PC 의 device 해시가 된다. 빈 표시 이름은 싣지 않는다.
        var codex = sender.Build(PushCategories.Usage, "t", "b", 4, null, TimeSpan.Zero, null,
            new PushAccount("codex", null, "  "));
        Assert.Equal(MobileAccountHash.For("codex", null, store.PcId).Hash, codex.AccountHash);
        Assert.Null(codex.AccountLabel);

        // 날씨·테스트처럼 계정이 없는 알림에는 계정 필드가 모두 없다(JSON 에도 나가지 않는다).
        var weather = sender.Build(PushCategories.Weather, "t", "b", 4, null, TimeSpan.Zero, null);
        Assert.Null(weather.Provider);
        Assert.Null(weather.AccountHash);
        Assert.Null(weather.AccountLabel);
        var json = JsonDocument.Parse(PushPayload.Serialize(weather)).RootElement;
        Assert.False(json.TryGetProperty("accountHash", out _));
        Assert.False(json.TryGetProperty("accountLabel", out _));
    }

    /// <summary>
    /// 계정 이름이 비정상적으로 길어도 알림은 나가야 한다(#265). 직렬화는 넘칠 때 본문·제목만 줄이므로,
    /// 이름을 Build 에서 한 줄·64자로 줄이지 않으면 암호화 한도를 넘겨 알림이 통째로 버려진다.
    /// </summary>
    [Fact]
    public void BuildCapsVeryLongAccountLabel_SoThePushStillFits()
    {
        var store = new MobileDeviceStore(_dir);
        var sender = new AppPushSender(store, new HttpClient(new FakeHandler(_ => HttpStatusCode.Created)), "HOME-PC");

        var message = sender.Build(PushCategories.Usage, "t", "b", 4, null, TimeSpan.Zero, null,
            new PushAccount("claude", "org|acct", "이름\n" + new string('가', 10_000)));

        Assert.NotNull(message.AccountLabel);
        Assert.Equal(AppPushSender.MaxAccountLabelLength, message.AccountLabel!.Length);
        Assert.StartsWith("이름 가", message.AccountLabel);
        Assert.True(PushPayload.Serialize(message).Length <= WebPushEncryption.MaxPlaintextBytes);
    }
}
