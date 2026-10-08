using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using ClaudeUsageTray.Services;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

public class ClaudeLoginLauncherTests
{
    private static readonly ClaudeLoginLauncher.LoginScriptMessages Messages = new(
        Installing: "설치 중 — it's installing",
        InstallFailed: "설치 실패 — it's failed",
        NotFoundAfterInstall: "찾지 못함 — can't find",
        Done: "완료 — you're done");

    [Fact]
    public void ResolveCliPath_ReturnsFirstMatchInDirectoryOrder()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(@"C:\second", "claude.exe"),
            Path.Combine(@"C:\third", "claude.cmd"),
        };

        var path = ClaudeLoginLauncher.ResolveCliPath([@"C:\first", @"C:\second", @"C:\third"], existing.Contains);

        Assert.Equal(Path.Combine(@"C:\second", "claude.exe"), path);
    }

    [Fact]
    public void ResolveCliPath_PrefersExeOverCmdWithinSameDirectory()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(@"C:\tools", "claude.cmd"),
            Path.Combine(@"C:\tools", "claude.exe"),
        };

        var path = ClaudeLoginLauncher.ResolveCliPath([@"C:\tools"], existing.Contains);

        Assert.Equal(Path.Combine(@"C:\tools", "claude.exe"), path);
    }

    // PATH 항목은 따옴표로 감싸이거나 비어 있을 수 있다(";;" 등).
    [Fact]
    public void ResolveCliPath_TrimsQuotesAndSkipsEmptyEntries()
    {
        var expected = Path.Combine(@"C:\Program Files\claude", "claude.exe");
        var probed = new List<string>();

        var path = ClaudeLoginLauncher.ResolveCliPath(["", "   ", "\"C:\\Program Files\\claude\""], candidate =>
        {
            probed.Add(candidate);
            return candidate == expected;
        });

        Assert.Equal(expected, path);
        Assert.All(probed, candidate => Assert.DoesNotContain("\"", candidate));
    }

    [Fact]
    public void ResolveCliPath_ReturnsNull_WhenNoCandidateExists()
    {
        Assert.Null(ClaudeLoginLauncher.ResolveCliPath([@"C:\a", @"C:\b"], _ => false));
    }

    [Theory]
    [InlineData(true, "-NoProfile -NoExit -EncodedCommand ")]
    [InlineData(false, "-NoProfile -EncodedCommand ")]
    public void BuildPowerShellArguments_EncodesScriptAsUtf16Base64(bool keepOpen, string expectedPrefix)
    {
        const string script = "& 'C:\\Users\\용 은\\claude.exe' auth login";

        var args = ClaudeLoginLauncher.BuildPowerShellArguments(script, keepOpen);

        Assert.StartsWith(expectedPrefix, args);
        Assert.Equal(script, Decode(args[expectedPrefix.Length..]));
    }

    [Fact]
    public void BuildLoginScript_WithCli_RunsLoginWithEscapedPath_AndDoesNotInstall()
    {
        var script = ClaudeLoginLauncher.BuildLoginScript(@"C:\Users\it's 용은\claude.exe", Messages);

        Assert.Contains(@"& 'C:\Users\it''s 용은\claude.exe' auth login", script);
        Assert.DoesNotContain("install.ps1", script);
        Assert.DoesNotContain("-EncodedCommand", script);
    }

    // PowerShell 은 곡선 따옴표(U+2018~U+201B)도 작은따옴표로 읽는다 — 이스케이프하지 않으면 문자열이 거기서 끝난다.
    [Fact]
    public void BuildLoginScript_EscapesCurlyQuotesInPath()
    {
        var script = ClaudeLoginLauncher.BuildLoginScript("C:\\Users\\O\u2019Brien \u2018x\u201A\u201B\\claude.exe", Messages);

        Assert.Contains("& 'C:\\Users\\O\u2019\u2019Brien \u2018\u2018x\u201A\u201A\u201B\u201B\\claude.exe' auth login", script);
    }

    // 설치 스크립트는 실패 시 exit 1 을 부르므로 자식 프로세스로 돌리고 종료 코드로 판단해야 창이 닫히지 않는다.
    [Fact]
    public void BuildLoginScript_WithoutCli_InstallsInChildProcessThenLogsIn()
    {
        var script = ClaudeLoginLauncher.BuildLoginScript(null, Messages);

        var match = Regex.Match(script, @"powershell\.exe -NoProfile -EncodedCommand (?<b64>[A-Za-z0-9+/=]+)");
        Assert.True(match.Success, script);
        var installer = Decode(match.Groups["b64"].Value);
        Assert.Contains(ClaudeLoginLauncher.InstallCommand, installer);
        Assert.Contains("Tls12", installer);

        Assert.Contains("if ($LASTEXITCODE -ne 0)", script);
        Assert.Contains(@"'.local\bin\claude.exe'", script);
        Assert.Contains("& $claude auth login", script);
        Assert.Contains("'설치 중 — it''s installing'", script);
        Assert.DoesNotContain("iex", script.Replace(match.Value, ""));
        Assert.DoesNotMatch(@"(?m)^\s*exit\b", script);
    }

    [Fact]
    public void BuildLoginScript_AlwaysEndsWithDoneHint()
    {
        Assert.Contains("'완료 — you''re done'", ClaudeLoginLauncher.BuildLoginScript(null, Messages));
        Assert.Contains("'완료 — you''re done'", ClaudeLoginLauncher.BuildLoginScript(@"C:\claude.exe", Messages));
    }

    /// <summary>생성한 두 스크립트가 실제 Windows PowerShell 파서에서 문법 오류 없이 읽히는지.</summary>
    [Theory]
    [Trait("Category", "Integration")]
    [InlineData(null)]
    [InlineData(@"C:\Users\it's 용 은\.local\bin\claude.exe")]
    [InlineData("C:\\Users\\O\u2019Brien\\.local\\bin\\claude.exe")]
    public void GeneratedScript_ParsesWithoutErrors_InWindowsPowerShell(string? cliPath)
    {
        var script = ClaudeLoginLauncher.BuildLoginScript(cliPath, Messages);
        var check = "$s = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('"
            + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
            + "')); $e = $null; [void][System.Management.Automation.Language.Parser]::ParseInput($s, [ref]$null, [ref]$e); "
            + "Write-Output (\"errors=\" + $e.Count); $e | ForEach-Object { Write-Output $_.Message }";

        var output = RunPowerShell(ClaudeLoginLauncher.BuildPowerShellArguments(check, keepOpen: false));

        Assert.Contains("errors=0", output);
    }

    /// <summary>
    /// 찾은 CLI 경로에 공백·한글·작은따옴표·곡선 따옴표가 있어도 그 경로 그대로 실행돼 "auth login" 인자를 받는지 —
    /// 가짜 claude.cmd 가 받은 인자를 파일로 남기게 해 실제로 돌려 본다(경로가 한 글자라도 바뀌면 실행되지 않는다).
    /// </summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void LoginScript_RunsResolvedCli_WithAuthLoginArguments_EvenWithUnusualPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"claude login it's O’Brien 용 은 {Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var fakeCli = Path.Combine(dir, "claude.cmd");
            var argsFile = Path.Combine(dir, "args.txt");
            File.WriteAllText(fakeCli, "@echo %*> \"%~dp0args.txt\"\r\n", Encoding.ASCII);

            var script = ClaudeLoginLauncher.BuildLoginScript(fakeCli, Messages);
            RunPowerShell(ClaudeLoginLauncher.BuildPowerShellArguments(script, keepOpen: false));

            Assert.True(File.Exists(argsFile), "가짜 CLI 가 실행되지 않았다.");
            Assert.Equal("auth login", File.ReadAllText(argsFile).Trim());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 정리 실패는 테스트 결과와 무관 */ }
        }
    }

    private static string Decode(string base64) => Encoding.Unicode.GetString(Convert.FromBase64String(base64));

    private static string RunPowerShell(string arguments)
    {
        using var proc = Process.Start(new ProcessStartInfo("powershell.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        })!;
        var output = proc.StandardOutput.ReadToEnd();
        Assert.True(proc.WaitForExit(60_000), "PowerShell 이 시간 안에 끝나지 않았다.");
        return output;
    }
}
