using System.IO;
using System.Text.Json;
using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

/// <summary>
/// 휴대폰 프로토콜 형식 고정(#220). 앱(Kotlin)이 같은 픽스처를 파싱하므로, 여기서 직렬화 결과가 바뀌면
/// 앱 쪽 파서도 함께 고쳐야 한다는 신호다. 의도한 변경이면 <c>CUT_UPDATE_GOLDEN=1</c> 로 다시 쓴다.
/// </summary>
public class MobileProtocolGoldenTests
{
    private static string FixtureDir()
    {
        // 저장소 밖에서 빌드한 테스트(예: 리눅스 검증용 하네스)는 경로를 환경 변수로 받는다.
        if (Environment.GetEnvironmentVariable("CUT_REPO_ROOT") is { Length: > 0 } root)
            return Path.Combine(root, "docs", "mobile-protocol");
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "claude-usage-tray-windows.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "docs", "mobile-protocol");
    }

    internal static void AssertGoldenFile(string name, string actual) => AssertGolden(name, actual);

    private static void AssertGolden(string name, string actual)
    {
        var path = Path.Combine(FixtureDir(), name);
        if (Environment.GetEnvironmentVariable("CUT_UPDATE_GOLDEN") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual);
        }
        Assert.Equal(File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd(), actual.ReplaceLineEndings("\n").TrimEnd());
    }

    private static readonly JsonSerializerOptions Pretty = new(MobileProtocol.Json) { WriteIndented = true };

    [Fact]
    public void SnapshotMatchesFixture() =>
        AssertGolden("usage-snapshot.v1.json", JsonSerializer.Serialize(MobileProtocolFixtures.Snapshot(), Pretty));

    [Fact]
    public void OfferMatchesFixture() =>
        AssertGolden("pairing-offer.v1.json", JsonSerializer.Serialize(MobileProtocolFixtures.Offer(), Pretty));

    [Fact]
    public void PairingLinkMatchesFixture() =>
        AssertGolden("pairing-link.v1.txt", PairingLink.Build(MobileProtocolFixtures.Offer()));

    [Fact]
    public void EndpointsMatchFixture() =>
        AssertGolden("endpoints.v1.json", JsonSerializer.Serialize(
            new EndpointsResponse(MobileProtocol.Schema, MobileProtocolFixtures.Endpoints()), Pretty));

    [Fact]
    public void PairingLinkRoundTrips()
    {
        var link = PairingLink.Build(MobileProtocolFixtures.Offer());
        Assert.StartsWith(PairingLink.Prefix, link);
        var json = PairingLink.DecodeJson(link);
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(1, doc.RootElement.GetProperty("cu").GetInt32());
        Assert.Equal(MobileProtocolFixtures.Endpoints().Count, doc.RootElement.GetProperty("endpoints").GetArrayLength());
    }

    [Fact]
    public void AccountHashMergesSameAccountAcrossPcsButNotUnknownAccounts()
    {
        var a = MobileAccountHash.For("claude", "org|acct", "pc-a");
        var b = MobileAccountHash.For("claude", "org|acct", "pc-b");
        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(MobileAccountScope.Account, a.Scope);

        // 계정을 모르는 PC 끼리는 합쳐지지 않는다(다른 계정일 수 있으므로).
        var u1 = MobileAccountHash.For("antigravity", null, "pc-a");
        var u2 = MobileAccountHash.For("antigravity", "  ", "pc-b");
        Assert.NotEqual(u1.Hash, u2.Hash);
        Assert.Equal(MobileAccountScope.Device, u1.Scope);

        // 같은 키라도 provider 가 다르면 다른 계정이다.
        Assert.NotEqual(a.Hash, MobileAccountHash.For("codex", "org|acct", "pc-a").Hash);
        Assert.Matches("^[0-9a-f]{64}$", a.Hash);
    }
}
