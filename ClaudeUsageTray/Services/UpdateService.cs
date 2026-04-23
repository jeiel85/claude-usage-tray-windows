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
    /// Launches the Updater with download URL and exits.
    /// The Updater will: download exe → verify SHA256 → wait for process → copy → restart.
    /// Uses PowerShell script to avoid SmartScreen warnings.
    /// </summary>
    public void ApplyUpdateAsync(string downloadUrl, string sha256Url = "")
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray.exe");
        var currentDir = Path.GetDirectoryName(currentExe) ?? ".";

        // Find Updater.exe (bundled with this app in the same directory)
        var updaterPath = Path.Combine(currentDir, "ClaudeUsageTray-Updater.exe");
        if (!File.Exists(updaterPath))
        {
            // Fallback: look in app base directory
            updaterPath = Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray-Updater.exe");
        }

        if (!File.Exists(updaterPath))
        {
            // If updater is still missing, we can't auto-update. 
            // Open release page as fallback.
            Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true });
            return;
        }

        // Escape paths for PowerShell (escapes single quotes for PS string)
        string Esc(string? s) => (s ?? "").Replace("'", "''");

        // Create PowerShell script to launch Updater
        var ps1Path = Path.Combine(Path.GetTempPath(), $"claude_update_{Guid.NewGuid():N}.ps1");
        
        // Use an array of arguments for reliability
        var psCommand = $@"
$args_arr = @('{Esc(downloadUrl)}', '{Esc(sha256Url)}', '{Esc(currentExe)}', '{Esc(currentDir)}')
Start-Process -FilePath '{Esc(updaterPath)}' -ArgumentList $args_arr -WindowStyle Hidden
Remove-Item -Path '{ps1Path}' -Force -ErrorAction SilentlyContinue
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
            // Fallback: direct launch if PowerShell fails
            Process.Start(new ProcessStartInfo(updaterPath, $"\"{downloadUrl}\" \"{sha256Url}\" \"{currentExe}\" \"{currentDir}\"")
            {
                UseShellExecute = true
            });
        }

        // Exit this process so Updater can proceed
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }
}
