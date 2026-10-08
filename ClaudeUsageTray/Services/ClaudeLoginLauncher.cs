using System.Diagnostics;
using System.IO;
using System.Text;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 로그인 만료·토큰 없음 상태에서 사용자가 터미널을 직접 열지 않아도 되도록 새 PowerShell 창에서
/// <c>claude auth login</c> 을 실행한다(#180). CLI 가 없으면 공식 설치 스크립트로 먼저 설치한다.
/// 로그인이 끝나면 Claude Code 가 자격 파일을 다시 쓰고, <see cref="CredentialService.CredentialsChanged"/> 가
/// 이를 감지해 새로고침이 이어진다.
/// </summary>
public static class ClaudeLoginLauncher
{
    /// <summary>공식 문서(code.claude.com/docs/en/setup)의 Windows PowerShell 네이티브 설치 명령.</summary>
    internal const string InstallCommand = "irm https://claude.ai/install.ps1 | iex";

    // PATHEXT 우선순위대로 — 네이티브 설치는 claude.exe, npm 전역 설치는 claude.cmd 셸 스크립트를 둔다.
    private static readonly string[] CliFileNames = ["claude.exe", "claude.cmd"];

    /// <summary>
    /// 설치된 claude CLI 의 전체 경로. 찾지 못하면 null — 탐색 중 오류도 "못 찾음" 으로 본다(설치 경로로 이어질 뿐).
    /// PATH 에 응답 없는 네트워크 경로가 있으면 오래 걸릴 수 있으므로 UI 스레드에서 부르지 않는다.
    /// </summary>
    public static string? FindCli()
    {
        try
        {
            return ResolveCliPath(GetSearchDirectories(), File.Exists);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 새 터미널 창에서 로그인을 시작한다. <paramref name="cliPath"/> 가 null 이면 공식 설치 스크립트로 먼저 설치한다.
    /// 실패하면 false 와 원인을 돌려준다.
    /// </summary>
    public static bool TryLaunch(string? cliPath, out string? error)
    {
        try
        {
            var script = BuildLoginScript(cliPath, new LoginScriptMessages(
                Installing: Loc.ClaudeCliInstalling,
                InstallFailed: Loc.ClaudeCliInstallFailed,
                NotFoundAfterInstall: Loc.ClaudeCliNotFoundAfterInstall,
                Done: Loc.ClaudeLoginTerminalDone));
            // UseShellExecute 로 띄워야 GUI 프로세스에서도 새 콘솔 창이 생긴다(Windows 11 은 기본 터미널 설정을 따른다).
            using var process = Process.Start(new ProcessStartInfo("powershell.exe", BuildPowerShellArguments(script, keepOpen: true))
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            });
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal sealed record LoginScriptMessages(string Installing, string InstallFailed, string NotFoundAfterInstall, string Done);

    /// <summary>
    /// 새 창에서 실행할 PowerShell 스크립트. CLI 를 찾았으면 바로 로그인하고, 없으면 설치 후 로그인한다.
    /// 공식 설치 스크립트는 실패 시 <c>exit 1</c> 을 부르므로 같은 세션에서 돌리면 오류를 읽기 전에 창이 닫힌다 —
    /// 자식 PowerShell 로 분리해 종료 코드로만 성패를 판단한다.
    /// 설치 직후에는 이 세션의 PATH 가 갱신되지 않으므로 네이티브 설치 위치의 전체 경로로 실행한다.
    /// </summary>
    internal static string BuildLoginScript(string? cliPath, LoginScriptMessages messages)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(cliPath))
        {
            sb.AppendLine($"& {PsQuote(cliPath)} auth login");
        }
        else
        {
            // Windows 10 의 PowerShell 5.1 은 환경에 따라 TLS 1.2 가 꺼져 있어 claude.ai 접속이 실패할 수 있다.
            var installer = "[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12; "
                + InstallCommand;
            sb.AppendLine($"Write-Host {PsQuote(messages.Installing)} -ForegroundColor Cyan");
            sb.AppendLine($"powershell.exe -NoProfile -EncodedCommand {Encode(installer)}");
            sb.AppendLine("if ($LASTEXITCODE -ne 0) {");
            sb.AppendLine($"    Write-Host {PsQuote(messages.InstallFailed)} -ForegroundColor Red");
            sb.AppendLine("} else {");
            sb.AppendLine("    $claude = Join-Path $env:USERPROFILE '.local\\bin\\claude.exe'");
            sb.AppendLine("    if (-not (Test-Path -LiteralPath $claude)) {");
            sb.AppendLine("        $claude = (Get-Command claude -ErrorAction SilentlyContinue | Select-Object -First 1).Source");
            sb.AppendLine("    }");
            sb.AppendLine("    if ($claude) {");
            sb.AppendLine("        & $claude auth login");
            sb.AppendLine("    } else {");
            sb.AppendLine($"        Write-Host {PsQuote(messages.NotFoundAfterInstall)} -ForegroundColor Red");
            sb.AppendLine("    }");
            sb.AppendLine("}");
        }
        sb.AppendLine($"Write-Host ''; Write-Host {PsQuote(messages.Done)} -ForegroundColor DarkGray");
        return sb.ToString();
    }

    /// <summary>
    /// powershell.exe 인자. 스크립트를 <c>-EncodedCommand</c>(UTF-16LE Base64)로 넘겨 공백·한글·따옴표가 든
    /// 경로도 명령줄 인용 규칙과 무관하게 그대로 전달한다. <paramref name="keepOpen"/> 이면 창을 남겨
    /// 로그인 결과(실패 메시지 포함)를 사용자가 읽을 수 있게 한다.
    /// </summary>
    internal static string BuildPowerShellArguments(string script, bool keepOpen) =>
        $"-NoProfile{(keepOpen ? " -NoExit" : "")} -EncodedCommand {Encode(script)}";

    /// <summary>
    /// 검색 디렉터리를 순서대로 훑어 처음 발견한 claude 실행 파일의 절대 경로. 없으면 null.
    /// PATH 의 상대 항목(".", "tools")은 이 프로세스의 현재 폴더 기준으로 찾아지지만 터미널은 사용자 폴더에서 열리므로,
    /// 찾은 경로를 절대 경로로 바꿔 돌려준다.
    /// </summary>
    internal static string? ResolveCliPath(IEnumerable<string> directories, Func<string, bool> fileExists)
    {
        foreach (var raw in directories)
        {
            var dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            foreach (var name in CliFileNames)
            {
                var candidate = Path.Combine(dir, name);
                if (fileExists(candidate)) return Path.GetFullPath(candidate);
            }
        }
        return null;
    }

    private static string Encode(string script) => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    // PowerShell 작은따옴표 문자열($ 등은 해석되지 않는다). PowerShell 은 ' 뿐 아니라 곡선 따옴표(U+2018~U+201B)도
    // 작은따옴표로 읽으므로(O’Brien 같은 사용자 폴더) 모두 두 번 써서 이스케이프한다.
    private static string PsQuote(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('\'');
        foreach (var ch in value)
        {
            if (ch is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(ch);
            sb.Append(ch);
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>
    /// 이 프로세스의 PATH 는 앱 시작 시점에 고정돼 그 뒤 설치한 CLI 를 모른다. 레지스트리의 사용자·시스템
    /// PATH 를 다시 읽고, PATH 에 등록하지 않은 경우를 위해 공식 설치 위치도 마지막에 덧붙인다.
    /// </summary>
    private static IEnumerable<string> GetSearchDirectories()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var paths = new[]
        {
            Environment.GetEnvironmentVariable("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
        };
        foreach (var path in paths)
        {
            if (string.IsNullOrEmpty(path)) continue;
            foreach (var dir in Environment.ExpandEnvironmentVariables(path).Split(Path.PathSeparator))
                yield return dir;
        }

        yield return Path.Combine(userProfile, ".local", "bin"); // 네이티브 설치 프로그램
        yield return Path.Combine(appData, "npm");               // npm install -g
    }
}
