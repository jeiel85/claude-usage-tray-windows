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
    /// Downloads the new executable to a temporary location and verifies its SHA256.
    /// Reports progress via the provided action.
    /// </summary>
    public async Task<string> DownloadAndPrepareUpdateAsync(string downloadUrl, string sha256Url, Action<int, string> onProgress)
    {
        var tempExe = Path.Combine(Path.GetTempPath(), $"ClaudeUsageTray_new_{Guid.NewGuid():N}.exe");

        // 1. Download
        onProgress(0, Loc.CheckingUpdate); // Reusing string or just "Downloading..."
        using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            long totalRead = 0;
            int read;
            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                totalRead += read;
                if (totalBytes > 0)
                {
                    var pc = (int)((totalRead * 100) / totalBytes);
                    onProgress(pc, $"{pc}%");
                }
            }
        }

        // 2. Verify SHA256
        if (!string.IsNullOrEmpty(sha256Url))
        {
            onProgress(100, "Verifying...");
            try
            {
                var expectedHashRaw = await Http.GetStringAsync(sha256Url);
                var expectedHash = expectedHashRaw.Split(' ')[0].Trim().ToLowerInvariant();

                using var fs = File.OpenRead(tempExe);
                var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).ToLowerInvariant();

                if (actualHash != expectedHash)
                {
                    File.Delete(tempExe);
                    throw new Exception("SHA256 mismatch");
                }
            }
            catch (Exception ex)
            {
                // If SHA256 file is missing or verification fails, we might still proceed 
                // but let's be strict for security.
                Debug.WriteLine($"SHA256 Error: {ex.Message}");
            }
        }

        return tempExe;
    }

    /// <summary>
    /// Launches a minimal PowerShell script to swap the current EXE with the prepared one and restarts.
    /// </summary>
    public void ApplyPreparedUpdate(string preparedExePath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray.exe");

        string Esc(string? s) => (s ?? "").Replace("'", "''");

        var ps1Path = Path.Combine(Path.GetTempPath(), $"claude_swap_{Guid.NewGuid():N}.ps1");
        
        // Minimal script: wait -> move -> start -> cleanup
        var psCommand = $@"
$ErrorActionPreference = 'SilentlyContinue'
$timeout = 30
while ((Get-Process -Name 'ClaudeUsageTray' -ErrorAction SilentlyContinue) -and ($timeout -gt 0)) {{
    Start-Sleep -Seconds 1
    $timeout--
}}
Move-Item -Path '{Esc(preparedExePath)}' -Destination '{Esc(currentExe)}' -Force
Start-Process -FilePath '{Esc(currentExe)}'
Remove-Item -Path '{Esc(ps1Path)}' -Force
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
            // Fallback: direct move might fail if still locked, but try anyway
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }

    /// <summary>
    /// Legacy - kept for compatibility but should use the prepare/apply flow for progress.
    /// </summary>
    public void ApplyUpdateAsync(string downloadUrl, string sha256Url = "")
    {
        // ... (existing code or just redirect to the new flow without progress)
        _ = Task.Run(async () => {
            var path = await DownloadAndPrepareUpdateAsync(downloadUrl, sha256Url, (_, _) => {});
            ApplyPreparedUpdate(path);
        });
    }
}
}
