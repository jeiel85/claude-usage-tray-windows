using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 휴대폰 API 의 인바운드 방화벽 규칙(Homebase <c>MobileFirewall</c>, DECISIONS M-14·M-15).
/// 두 규칙 모두 이 exe·TCP 포트로만 한정한다.
/// <list type="bullet">
/// <item><see cref="LanRuleName"/>: 원격 LocalSubnet, <b>개인(Private) 프로필만</b> — 카페·공항 Wi-Fi(공용)에는 열리지 않는다.</item>
/// <item><see cref="TailscaleRuleName"/>: 원격 100.64.0.0/10, 모든 프로필(WireGuard 터널 안이므로).</item>
/// </list>
///
/// 왜 트레이가 직접 하나: Homebase 는 LocalSystem 서비스(Agent)가 규칙을 관리하지만 이 앱은 사용자 권한 트레이 하나뿐이다.
/// 규칙이 없으면 Windows 가 처음 수신할 때 "액세스 허용" 대화상자를 띄우는데, 거기서 허용하면 범위 제한 없이 이 exe 의
/// 모든 포트가 열리고 취소하면 차단 규칙이 남는다. 그래서 사용자가 버튼을 한 번 눌러(UAC) 좁은 규칙을 직접 넣게 한다.
/// </summary>
public static class MobileFirewall
{
    public const string LanRuleName = "Claude Usage Tray Mobile (LAN)";
    public const string TailscaleRuleName = "Claude Usage Tray Mobile (Tailscale)";

    private const int ErrorCancelled = 1223;

    public enum InstallResult { Installed, Cancelled, Failed }

    /// <summary>
    /// Input : 포트, 이 exe 경로
    /// Output: 두 규칙이 모두 이 exe·포트를 가리키는가
    /// 핵심 로직: <c>netsh advfirewall firewall show rule</c> 은 관리자 권한 없이 읽힌다. 출력의 항목 이름은 Windows 언어로
    ///           바뀌므로 이름 대신 "exe 경로와 포트 문자열이 들어 있는가" 로 판단한다. exe 를 옮겼으면(경로가 다르면)
    ///           false 가 되어 다시 추가하라고 안내된다.
    /// </summary>
    public static bool AreRulesInstalled(int port, string? exePath)
    {
        if (string.IsNullOrEmpty(exePath) || !OperatingSystem.IsWindows()) return false;
        return RuleMatches(LanRuleName, port, exePath) && RuleMatches(TailscaleRuleName, port, exePath);
    }

    private static bool RuleMatches(string name, int port, string exePath)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh.exe", $"advfirewall firewall show rule name=\"{name}\" verbose")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = OemEncoding(),
            });
            if (p is null) return false;
            var output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(10_000)) { p.Kill(); return false; }
            return p.ExitCode == 0
                && output.Contains(exePath, StringComparison.OrdinalIgnoreCase)
                && output.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// netsh 는 콘솔 OEM 코드 페이지(한국어 Windows 는 949)로 출력한다. UTF-8 로 읽으면 한글 exe 경로가 깨져
    /// 규칙이 있는데도 "없음" 으로 판단하므로 OEM 코드 페이지로 읽는다.
    /// </summary>
    private static Encoding OemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// Input : 포트, exe 경로
    /// Output: 설치됨 / 사용자가 UAC 를 취소함 / 실패
    /// 핵심 로직: 관리자 PowerShell 을 <c>-EncodedCommand</c>(UTF-16 base64)로 띄운다. 배치·cmd 인용 규칙은 한글·공백 경로에서
    ///           깨지기 쉽다(AGENTS 레슨: 한글 경로·PowerShell 전환). 같은 이름의 옛 규칙은 지우고 새로 넣는다.
    /// </summary>
    public static async Task<InstallResult> InstallElevatedAsync(int port, string exePath) =>
        await RunElevatedAsync(BuildInstallScript(port, exePath));

    /// <summary>기능을 끈 사용자가 규칙도 지우고 싶을 때.</summary>
    public static async Task<InstallResult> RemoveElevatedAsync() =>
        await RunElevatedAsync(BuildRemoveScript());

    internal static string BuildInstallScript(int port, string exePath)
    {
        var exe = PsQuote(exePath);
        var sb = new StringBuilder(BuildRemoveScript());
        sb.Append($"New-NetFirewallRule -DisplayName {PsQuote(LanRuleName)} -Direction Inbound -Action Allow -Protocol TCP ")
          .Append($"-LocalPort {port} -RemoteAddress LocalSubnet -Profile Private -Program {exe} | Out-Null;");
        sb.Append($"New-NetFirewallRule -DisplayName {PsQuote(TailscaleRuleName)} -Direction Inbound -Action Allow -Protocol TCP ")
          .Append($"-LocalPort {port} -RemoteAddress 100.64.0.0/10 -Profile Any -Program {exe} | Out-Null;");
        return sb.ToString();
    }

    internal static string BuildRemoveScript() =>
        $"Remove-NetFirewallRule -DisplayName {PsQuote(LanRuleName)} -ErrorAction SilentlyContinue;" +
        $"Remove-NetFirewallRule -DisplayName {PsQuote(TailscaleRuleName)} -ErrorAction SilentlyContinue;";

    /// <summary>PowerShell 작은따옴표 문자열 — 안의 작은따옴표만 두 번 쓰면 된다(변수·이스케이프 해석 없음).</summary>
    internal static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private static async Task<InstallResult> RunElevatedAsync(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop';" + script));
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (p is null) return InstallResult.Failed;
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? InstallResult.Installed : InstallResult.Failed;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return InstallResult.Cancelled;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return InstallResult.Failed;
        }
    }
}
