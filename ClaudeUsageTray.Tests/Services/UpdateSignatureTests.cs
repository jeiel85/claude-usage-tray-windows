using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ClaudeUsageTray.Services;
using Xunit;
using Xunit.Abstractions;

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
public class UpdateSignatureLiveReleaseTests(ITestOutputHelper output)
{
    [Fact]
    public async Task NewestSignedReleaseVerifiesWithTheEmbeddedKey()
    {
        // 약 34MB exe 를 받는다 — 30초로는 1MB/s 회선에서 늘 시간 초과였다(#302).
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.Add("User-Agent", "ClaudeUsageTray-Tests");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://api.github.com/repos/jeiel85/claude-usage-tray-windows/releases?per_page=30");
        // PR CI 는 읽기 전용 워크플로 토큰을 넘긴다(토큰이 없으면 러너 IP 공용 한도에 걸린다). API 요청에만 붙인다.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await http.SendAsync(request);
        // 한도 초과는 이 PC·러너의 사정이지 서명이 틀린 것이 아니다. xUnit 2 에는 실행 중 건너뛰기가 없어 확인 없이 끝내고
        // 로그에만 남긴다 — 빨갛게 뜨는 것은 서명이 실제로 맞지 않을 때뿐이게.
        if (IsRateLimited(response))
        {
            output.WriteLine($"GitHub API rate limited ({(int)response.StatusCode}); signature not checked.");
            return;
        }
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var update = UpdateService.SelectUpdate(doc.RootElement, new Version(0, 0, 0));
        if (update is null) return;

        var exe = await http.GetByteArrayAsync(update.downloadUrl);
        var signature = await http.GetStringAsync(update.signatureUrl);
        Assert.True(UpdateSignature.Verify(update.version, Convert.ToHexString(SHA256.HashData(exe)), signature),
            $"v{update.version} signature does not verify with the embedded key");
    }

    // GitHub 는 기본 한도 초과를 403 + X-RateLimit-Remaining: 0 으로, 보조 한도는 403·429 + Retry-After 로 알린다.
    private static bool IsRateLimited(HttpResponseMessage response) =>
        response.StatusCode == HttpStatusCode.TooManyRequests
        || (response.StatusCode == HttpStatusCode.Forbidden
            && (response.Headers.RetryAfter is not null
                || (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")));
}
