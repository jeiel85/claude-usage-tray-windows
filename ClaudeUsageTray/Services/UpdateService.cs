using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

public class UpdateService
{
    private const string Repo       = "jeiel85/claude-usage-tray-windows";
    private const string ApiUrl     = $"https://api.github.com/repos/{Repo}/releases/latest";
    private const string ReleasePage = $"https://github.com/{Repo}/releases/latest";

    private static readonly HttpClient Http = new();

    public record UpdateInfo(Version version, string downloadUrl, string sha256Url, string releaseNotes);

    static UpdateService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "ClaudeUsageTray-Updater");
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Returns UpdateInfo if a newer release exists, otherwise null.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var versionStr = tagName.TrimStart('v');
            if (!Version.TryParse(versionStr, out var latest)) return null;
            if (latest <= CurrentVersion) return null;

            var releaseNotes = root.TryGetProperty("body", out var bodyEl)
                ? bodyEl.GetString() ?? "" : "";

            string? exeUrl = null;
            string? sha256Url = null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url  = asset.GetProperty("browser_download_url").GetString() ?? "";

                // ClaudeUsageTray.exe — 메인 앱 (Updater가 아닌 것만)
                if (name.Equals("ClaudeUsageTray.exe", StringComparison.OrdinalIgnoreCase))
                    exeUrl = url;
                // SHA256.txt 또는 .sha256 파일 모두 지원
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("SHA256.txt", StringComparison.OrdinalIgnoreCase))
                    sha256Url = url;
            }

            if (exeUrl is null) return null;

            return new UpdateInfo(latest, exeUrl,
                sha256Url ?? "", releaseNotes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Launches a pure PowerShell script to handle the update process and exits.
    /// The script will: download -> verify SHA256 -> wait for exit -> replace -> restart.
    /// This removes the need for a separate Updater.exe.
    /// </summary>
    public void ApplyUpdateAsync(string downloadUrl, string sha256Url = "")
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray.exe");
        var currentDir = Path.GetDirectoryName(currentExe) ?? ".";

        // Escape paths for PowerShell (escapes single quotes)
        string Esc(string? s) => (s ?? "").Replace("'", "''");

        var ps1Path = Path.Combine(Path.GetTempPath(), $"claude_update_{Guid.NewGuid():N}.ps1");
        var tempExe = Path.Combine(Path.GetTempPath(), $"ClaudeUsageTray_new_{Guid.NewGuid():N}.exe");

        // The pure PowerShell update script
        var psCommand = $@"
$ErrorActionPreference = 'Stop'
$tempExe = '{Esc(tempExe)}'
$targetExe = '{Esc(currentExe)}'
$downloadUrl = '{Esc(downloadUrl)}'
$sha256Url = '{Esc(sha256Url)}'

try {{
    # 1. Download new executable
    Invoke-WebRequest -Uri $downloadUrl -OutFile $tempExe -UseBasicParsing

    # 2. Verify SHA256 if provided
    if ($sha256Url) {{
        $expectedHash = (Invoke-WebRequest -Uri $sha256Url -UseBasicParsing).Content.Split(' ')[0].Trim().ToLower()
        $actualHash = (Get-FileHash $tempExe -Algorithm SHA256).Hash.ToLower()
        if ($actualHash -ne $expectedHash) {{
            throw 'SHA256 verification failed'
        }}
    }}

    # 3. Wait for the main process to exit (max 30s)
    $timeout = 30
    while ((Get-Process -Name 'ClaudeUsageTray' -ErrorAction SilentlyContinue) -and ($timeout -gt 0)) {{
        Start-Sleep -Seconds 1
        $timeout--
    }}

    # 4. Replace and Restart
    Move-Item -Path $tempExe -Destination $targetExe -Force
    Start-Process -FilePath $targetExe
}}
catch {{
    # Log error or notify user if needed (hidden for now)
    $_.Exception.Message | Out-File -FilePath (Join-Path $env:TEMP 'claude_update_error.log')
}}
finally {{
    Remove-Item -Path '{Esc(ps1Path)}' -Force -ErrorAction SilentlyContinue
}}
";

        try
        {
            File.WriteAllText(ps1Path, psCommand, System.Text.Encoding.UTF8);

            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1Path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // Fallback: Just open the release page if PowerShell fails
            Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true });
        }

        // Exit this process so the script can replace the file
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }
}
