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

    public record UpdateInfo(Version version, string downloadUrl, string sha256Url, string updaterUrl, string releaseNotes);

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
            string? updaterUrl = null;

            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url  = asset.GetProperty("browser_download_url").GetString() ?? "";

                if (name.Equals("ClaudeUsageTray.exe", StringComparison.OrdinalIgnoreCase))
                    exeUrl = url;
                else if (name.Equals("ClaudeUsageTray-Updater.exe", StringComparison.OrdinalIgnoreCase))
                    updaterUrl = url;
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("SHA256.txt", StringComparison.OrdinalIgnoreCase))
                    sha256Url = url;
            }

            if (exeUrl is null) return null;

            return new UpdateInfo(latest, exeUrl,
                sha256Url ?? "", updaterUrl ?? "", releaseNotes);
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
    public void ApplyUpdateAsync(string downloadUrl, string sha256Url = "", string updaterUrl = "")
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

        // Updater.exe가 없으면 릴리스 페이지 열기 (수동 다운로드 안내)
        if (!File.Exists(updaterPath))
        {
            System.Windows.MessageBox.Show(
                "업데이터(ClaudeUsageTray-Updater.exe)를 찾을 수 없습니다.\n\n" +
                "GitHub 릴리스 페이지에서 ClaudeUsageTray.exe와\n" +
                "ClaudeUsageTray-Updater.exe를 함께 다운로드해 주세요.\n\n" +
                "지금 브라우저에서 다운로드 페이지를 열겠습니다.",
                "업데이트",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);

            Process.Start(new ProcessStartInfo(ReleasePage) { UseShellExecute = true });
            return;
        }

        // Escape paths for PowerShell
        var escapedUpdater = updaterPath.Replace("'", "''");
        var escapedDownloadUrl = downloadUrl.Replace("'", "''");
        var escapedSha256Url = sha256Url.Replace("'", "''");
        var escapedCurrentExe = currentExe.Replace("'", "''");
        var escapedCurrentDir = currentDir.Replace("'", "''");
        var escapedUpdaterUrl = updaterUrl.Replace("'", "''");

        // Create PowerShell script to launch Updater
        var ps1Path = Path.Combine(Path.GetTempPath(), $"claude_update_{Guid.NewGuid():N}.ps1");
        var psCommand = $@"
Start-Process -FilePath '{escapedUpdater}' -ArgumentList '{escapedDownloadUrl}', '{escapedSha256Url}', '{escapedCurrentExe}', '{escapedCurrentDir}', '{escapedUpdaterUrl}'
Remove-Item -Path '{ps1Path}' -Force -ErrorAction SilentlyContinue
";

        try
        {
            File.WriteAllText(ps1Path, psCommand);

            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1Path}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        catch
        {
            // Fallback: direct launch if PowerShell fails
            Process.Start(new ProcessStartInfo(updaterPath,
                $"\"{downloadUrl}\" \"{sha256Url}\" \"{currentExe}\" \"{currentDir}\" \"{updaterUrl}\"")
            {
                UseShellExecute = true
            });
        }

        // Exit this process so Updater can proceed
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }
}
