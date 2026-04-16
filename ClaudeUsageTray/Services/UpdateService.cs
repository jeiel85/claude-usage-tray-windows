using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
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

                // Support both new format (ClaudeUsageTray.exe) and old format (ClaudeUsageTray-sc.exe)
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    exeUrl = url;
                else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
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
    /// Downloads the new exe, writes a batch updater to %TEMP%, launches it and exits.
    /// </summary>
    public async Task ApplyUpdateAsync(string downloadUrl, string sha256Url = "",
        IProgress<int>? progress = null)
    {
        var tempDir    = Path.GetTempPath();
        var newExePath = Path.Combine(tempDir, "ClaudeUsageTray_update.exe");
        var scriptPath = Path.Combine(tempDir, "claude_tray_update.bat");
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray.exe");

        // Download with progress reporting
        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0;
        using var srcStream = await response.Content.ReadAsStreamAsync();

        {
            using var destStream = new FileStream(newExePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long downloaded = 0;
            int  read;
            while ((read = await srcStream.ReadAsync(buffer)) > 0)
            {
                await destStream.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (totalBytes > 0)
                    progress?.Report((int)(downloaded * 100 / totalBytes));
            }
        } // destStream closed here — before SHA256 verification and before batch script

        // SHA256 verification — try to download sha256 file and verify
        if (!string.IsNullOrEmpty(sha256Url))
        {
            try
            {
                var sha256Raw = await Http.GetStringAsync(sha256Url);
                var expectedHash = sha256Raw.Split(' ')[0].Trim().ToLowerInvariant();

                using var exeStream = File.OpenRead(newExePath);
                var actualHash = Convert.ToHexString(SHA256.HashData(exeStream)).ToLowerInvariant();

                if (actualHash != expectedHash)
                {
                    File.Delete(newExePath);
                    throw new InvalidOperationException("SHA256 mismatch");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // sha256 fetch/parse failed — log and continue without verification
                // (old releases before sha256 support)
            }
        }

        // Batch script: wait for this process to exit, replace exe, restart
        var script = $"""
            @echo off
            timeout /t 2 /nobreak >nul
            copy /y "{newExePath}" "{currentExe}"
            if errorlevel 1 (
                echo [%date% %time%] copy failed: {newExePath} -^> {currentExe} >> "%TEMP%\claude_update_error.log"
                exit /b 1
            )
            start "" "{currentExe}"
            del "{newExePath}" 2>nul
            del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        // Double-quote the script path inside /c "..." so cmd.exe correctly handles
        // paths that contain spaces (e.g. username with a space in %TEMP%).
        // Without double-quoting: cmd.exe strips the outer quotes then parses on spaces,
        // causing silent failure when the TEMP path contains a space.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{scriptPath}\"\"")
        {
            CreateNoWindow = true
        });

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }
}
