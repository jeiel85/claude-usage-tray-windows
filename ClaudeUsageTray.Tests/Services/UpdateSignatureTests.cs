using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>업데이트 서명 형식(#290). 서명 스크립트(<c>tools/sign-release.ps1</c>)와 같은 메시지·형식이어야 한다.</summary>
public class UpdateSignatureTests
{
    private const string Hash = "3cd764882cb9f1ed95be49ac1e1b8f17104be85838e362994e6defa1e25e3eae";

    [Fact]
    public void MessageBindsTheVersionAndHashInAFixedFormat()
    {
        // 서명 스크립트가 같은 바이트를 만든다 — 바꾸면 두 쪽을 함께 바꾸고, 이전 버전 앱은 새 형식 서명을 거부한다.
        Assert.Equal("claude-usage-tray-update/v1\n1.67.3\n" + Hash + "\n",
            System.Text.Encoding.UTF8.GetString(UpdateSignature.Message(new Version(1, 67, 3), " " + Hash.ToUpperInvariant() + " ")));
    }

    [Fact]
    public void AcceptsOnlyTheSignedVersionAndHash()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = key.ExportSubjectPublicKeyInfoPem();
        var version = new Version(1, 67, 3);
        var sig = Sign(key, version, Hash);

        Assert.True(UpdateSignature.Verify(pem, version, Hash, sig));
        Assert.True(UpdateSignature.Verify(pem, version, Hash.ToUpperInvariant(), sig + "\n"));

        // 옛 서명 exe 를 더 높은 버전 태그로 다시 올리는 경우.
        Assert.False(UpdateSignature.Verify(pem, new Version(1, 68, 0), Hash, sig));
        // exe 를 바꾼 경우.
        Assert.False(UpdateSignature.Verify(pem, version, Hash.Replace('3', '4'), sig));
        // 다른 키(앱에 내장한 릴리스 키가 아님).
        Assert.False(UpdateSignature.Verify(version, Hash, sig));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void MalformedSignaturesAreRejectedWithoutThrowing(string? signature)
    {
        Assert.False(UpdateSignature.Verify(new Version(1, 0, 0), Hash, signature));
    }

    [Fact]
    public void EmbeddedReleaseKeyIsAP256PublicKey()
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(UpdateSignature.PublicKeyPem);
        Assert.Equal(256, key.KeySize);
        Assert.Throws<CryptographicException>(() => key.ExportParameters(includePrivateParameters: true));
    }

    internal static string Sign(ECDsa key, Version version, string hash) =>
        Convert.ToBase64String(key.SignData(UpdateSignature.Message(version, hash), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
}

/// <summary>
/// 실제 GitHub 릴리스: 서명이 붙은 가장 새 릴리스가 앱에 내장한 키로 검증되는가. 서명 스크립트·공개키·앱 검증이 서로 맞는지를
/// 실물로 확인한다(서명 전 릴리스만 있으면 확인할 것이 없어 통과).
/// </summary>
[Trait("Category", "Network")]
public class UpdateSignatureLiveReleaseTests
{
    [Fact]
    public async Task NewestSignedReleaseVerifiesWithTheEmbeddedKey()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("User-Agent", "ClaudeUsageTray-Tests");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(
            "https://api.github.com/repos/jeiel85/claude-usage-tray-windows/releases?per_page=30"));

        var update = UpdateService.SelectUpdate(doc.RootElement, new Version(0, 0, 0));
        if (update is null) return;

        var exe = await http.GetByteArrayAsync(update.downloadUrl);
        var signature = await http.GetStringAsync(update.signatureUrl);
        Assert.True(UpdateSignature.Verify(update.version, Convert.ToHexString(SHA256.HashData(exe)), signature),
            $"v{update.version} signature does not verify with the embedded key");
    }
}
