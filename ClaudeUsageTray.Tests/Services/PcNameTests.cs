using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>설정의 PC 이름(#271) 정리 규칙. 전역 별칭(<see cref="PcName.SetAlias"/>)은 다른 테스트와 겹치므로 건드리지 않는다.</summary>
public class PcNameTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  Home PC  ", "Home PC")]
    [InlineData("Home\r\n\tPC", "Home PC")]
    [InlineData("a\u0007b", "a b")]
    [InlineData("거실   데스크톱", "거실 데스크톱")]
    public void CleanTrimsAndFlattensToOneLine(string? input, string expected) =>
        Assert.Equal(expected, PcName.Clean(input));

    [Fact]
    public void CleanCutsAtTheLimitWithoutSplittingAnEmoji()
    {
        Assert.Equal(new string('x', PcName.MaxAliasLength), PcName.Clean(new string('x', 50), PcName.MaxAliasLength));
        // 31자 + 이모지(서로게이트 쌍 2칸) → 32자 한도에서 이모지를 반쪽으로 남기지 않는다.
        var name = new string('x', 31) + "\U0001F4BB";
        Assert.Equal(new string('x', 31), PcName.Clean(name, PcName.MaxAliasLength));
        Assert.Equal(name, PcName.Clean(name, 33));
    }

    [Fact]
    public void FixedSourceIgnoresTheGlobalNameAndBlankFallsBackToIt()
    {
        Assert.Equal("Desk", PcName.Source(" Desk ")());
        Assert.Equal(PcName.Current, PcName.Source(null)());
    }
}
