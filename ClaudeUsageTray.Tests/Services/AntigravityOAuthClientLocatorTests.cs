using System;
using System.IO;
using System.Linq;
using System.Text;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 바이너리에서 OAuth client 자격 증명을 뽑아내는 스캐너.
/// 실제 바이너리에서는 이 값들이 인접한 다른 문자열과 경계 없이 붙어 있어서, 문자 종류만으로 자르면
/// 옆 문자열을 함께 먹는다. 그 상황을 픽스처로 재현해 둔다.
/// </summary>
public sealed class AntigravityOAuthClientLocatorTests
{
    // 형식만 같은 가짜 값이다. 실제 자격 증명을 픽스처에 두면 저장소에 secret 이 남고,
    // Google 의 secret-scanning 정책상 폐기될 수 있다. 조각을 이어 붙이는 것도 같은 이유다.
    private const string IdSuffix = ".apps.googleusercontent.com";
    private static readonly string ClientId = "123456789012-" + new string('a', 30) + IdSuffix;
    private static readonly string ClientSecret = "GOCSPX-" + new string('b', 28);   // 접두사 + 28자

    private static Stream StreamOf(string text) => new MemoryStream(Encoding.ASCII.GetBytes(text));

    [Fact]
    public void ScanCandidates_FindsPair_WhenValuesAreGluedToNeighbouringStrings()
    {
        // "it" + client_id, secret + "cached" 처럼 앞뒤에 다른 문자열이 붙은 실제 배치.
        using var stream = StreamOf($"runtimeit{ClientId}\0somethingelse{ClientSecret}cachedtail");

        var pairs = AntigravityOAuthClientLocator.ScanCandidates(stream);

        var pair = Assert.Single(pairs);
        Assert.Equal(ClientId, pair.ClientId);
        Assert.Equal(ClientSecret, pair.ClientSecret);
    }

    [Fact]
    public void ScanCandidates_ReturnsEveryCombination_WhenBinaryHoldsSeveralOfEach()
    {
        string otherId = "987654321098-" + new string('c', 30) + IdSuffix;
        string otherSecret = "GOCSPX-" + new string('d', 28);

        using var stream = StreamOf($"{ClientId} {otherId} {ClientSecret} {otherSecret}");

        var pairs = AntigravityOAuthClientLocator.ScanCandidates(stream);

        // 바이너리에 짝 정보가 없으므로 조합을 모두 내놓고 호출부가 차례로 시도한다.
        Assert.Equal(4, pairs.Count);
        Assert.Contains(pairs, p => p.ClientId == ClientId && p.ClientSecret == ClientSecret);
        Assert.Contains(pairs, p => p.ClientId == otherId && p.ClientSecret == otherSecret);
    }

    [Fact]
    public void ScanCandidates_Deduplicates_WhenSameValueAppearsRepeatedly()
    {
        using var stream = StreamOf(string.Concat(Enumerable.Repeat($"{ClientId}|{ClientSecret}|", 5)));

        Assert.Single(AntigravityOAuthClientLocator.ScanCandidates(stream));
    }

    [Theory]
    // 숫자 프로젝트 번호가 너무 짧다
    [InlineData("123-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.apps.googleusercontent.com")]
    // 하이픈이 없다
    [InlineData("123456789012aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.apps.googleusercontent.com")]
    // 프로젝트 해시가 너무 짧다
    [InlineData("123456789012-abc.apps.googleusercontent.com")]
    public void ScanCandidates_IgnoresIds_ThatDoNotMatchGoogleFormat(string malformedId)
    {
        using var stream = StreamOf($"{malformedId} {ClientSecret}");

        Assert.Empty(AntigravityOAuthClientLocator.ScanCandidates(stream));
    }

    [Fact]
    public void ScanCandidates_IgnoresSecret_WhenBodyIsShorterThanGoogleFormat()
    {
        using var stream = StreamOf($"{ClientId} GOCSPX-tooshort!!");

        Assert.Empty(AntigravityOAuthClientLocator.ScanCandidates(stream));
    }

    [Fact]
    public void ScanCandidates_TakesFixedLengthBody_WhenSecretRunsIntoAdjacentText()
    {
        // secret 뒤에 문자가 계속 이어져도 접두사 뒤 28자에서 끊어야 한다.
        using var stream = StreamOf($"{ClientId} {ClientSecret}TrailingGarbageAAAA");

        var pair = Assert.Single(AntigravityOAuthClientLocator.ScanCandidates(stream));
        Assert.Equal(ClientSecret, pair.ClientSecret);
        Assert.Equal(35, pair.ClientSecret.Length);
    }

    [Fact]
    public void ScanCandidates_FindsValues_WhenTheyStraddleTheReadChunkBoundary()
    {
        // 청크는 4MB 단위로 읽는다. 경계에 걸친 문자열을 놓치면 실제 바이너리에서 간헐적으로 실패한다.
        const int chunkSize = 4 * 1024 * 1024;
        var padding = new string('x', chunkSize - 20);

        using var stream = StreamOf($"{padding}{ClientId} {ClientSecret} tail");

        var pair = Assert.Single(AntigravityOAuthClientLocator.ScanCandidates(stream));
        Assert.Equal(ClientId, pair.ClientId);
        Assert.Equal(ClientSecret, pair.ClientSecret);
    }

    [Fact]
    public void ScanCandidates_ReturnsEmpty_WhenNeitherValueIsPresent()
    {
        using var stream = StreamOf("no credentials in this binary at all");

        Assert.Empty(AntigravityOAuthClientLocator.ScanCandidates(stream));
    }
}
