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

    static UpdateService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "ClaudeUsageTray-Updater");
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Returns (latestVersion, downloadUrl, releaseNotes) if a newer release exists, otherwise null.
    /// </summary>
    public async Task<(Version version, string downloadUrl, string releaseNotes)?> CheckForUpdateAsync()
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

            // Find the .exe asset
            foreach (var asset in root.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    return (latest, url, releaseNotes);
                }
            }

            // Fallback: no exe asset, link to release page
            return (latest, ReleasePage, releaseNotes);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Downloads the new exe, writes a batch updater to %TEMP%, launches it and exits.
    /// </summary>
    public async Task ApplyUpdateAsync(string downloadUrl)
    {
        var tempDir    = Path.GetTempPath();
        var newExePath = Path.Combine(tempDir, "ClaudeUsageTray_update.exe");
        var scriptPath = Path.Combine(tempDir, "claude_tray_update.bat");
        var currentExe = Process.GetCurrentProcess().MainModule!.FileName;

        // Download
        var bytes = await Http.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(newExePath, bytes);

        // Batch script: wait for this process to exit, replace exe, restart
        var script = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            copy /y "{newExePath}" "{currentExe}"
            start "" "{currentExe}"
            del "{newExePath}"
            del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{scriptPath}\"")
        {
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }
}
