using ClaudeUsageTray.Services.Mobile;
using Xunit;

namespace ClaudeUsageTray.Tests.Services.Mobile;

// 방화벽 규칙 스크립트(#220): 한글·공백·작은따옴표가 든 exe 경로도 PowerShell 문자열 하나로 안전하게 들어가야 한다.
public class MobileFirewallTests
{
    [Fact]
    public void QuotesPathsWithApostrophesAndKoreanSafely()
    {
        Assert.Equal("'C:\\사용자\\O''Brien\\ClaudeUsageTray.exe'", MobileFirewall.PsQuote("C:\\사용자\\O'Brien\\ClaudeUsageTray.exe"));
    }

    [Fact]
    public void InstallScriptScopesBothRulesToThisExeAndPort()
    {
        var script = MobileFirewall.BuildInstallScript(47830, "C:\\Apps\\ClaudeUsageTray.exe");
        // 같은 이름의 옛 규칙을 먼저 지운다(경로가 바뀐 경우).
        Assert.StartsWith("Remove-NetFirewallRule", script);
        Assert.Contains("-RemoteAddress LocalSubnet -Profile Private -Program 'C:\\Apps\\ClaudeUsageTray.exe'", script);
        Assert.Contains("-RemoteAddress 100.64.0.0/10 -Profile Any -Program 'C:\\Apps\\ClaudeUsageTray.exe'", script);
        Assert.Equal(2, script.Split("-LocalPort 47830").Length - 1);
    }
}
