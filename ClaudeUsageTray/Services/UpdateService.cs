using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClaudeUsageTray.Services;

/// <summary>업데이트 확인 실패 원인 분류 — UI 가 카테고리별로 안내문구를 라우팅한다.</summary>
public enum UpdateCheckErrorKind
{
    Unknown,
    Network,     // 네트워크 도달 불가
    Timeout,     // 응답 지연
    RateLimit,   // GitHub API 무인증 60/h 초과 (HTTP 403 + "rate limit")
    ApiError,    // 그 외 GitHub API 측 에러 (HTTP 4xx/5xx)
}

/// <summary>분류된 업데이트 확인 예외 — Kind 별로 사용자 안내문구가 갈라진다.</summary>
public class UpdateCheckException : Exception
{
    public UpdateCheckErrorKind Kind { get; }
    public int? StatusCode { get; }
    public string? RetryAtLocal { get; }   // RateLimit 시 사용자 시간대 "HH:mm" 문자열

    public UpdateCheckException(string message, UpdateCheckErrorKind kind,
                                int? statusCode = null, string? retryAtLocal = null,
                                Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
        RetryAtLocal = retryAtLocal;
    }
}

public class UpdateService
{
    private const string Repo        = "jeiel85/claude-usage-tray-windows";
    private const string ApiListUrl  = $"https://api.github.com/repos/{Repo}/releases?per_page=30";
    public  const string ReleasePage = $"https://github.com/{Repo}/releases/latest";

    // 100초 기본은 너무 길어 사용자가 멈춘 줄 안다 — 15초로 단축
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Verification logic
    public record UpdateInfo(Version version, string downloadUrl, string sha256Url, string releaseNotes);

    static UpdateService()
    {
        Http.DefaultRequestHeaders.Add("User-Agent", "ClaudeUsageTray-Updater");
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>
    /// Returns UpdateInfo if a newer release exists, null if already up to date.
    /// Throws <see cref="UpdateCheckException"/> on classified failures so callers can route messaging by Kind.
    /// Release notes include all versions between current and latest.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        HttpResponseMessage response;
        string rawJson;
        try
        {
            response = await Http.GetAsync(ApiListUrl);
            rawJson = await response.Content.ReadAsStringAsync();
        }
        catch (TaskCanceledException ex)
        {
            throw new UpdateCheckException(ex.Message, UpdateCheckErrorKind.Timeout, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateCheckException(ex.Message, UpdateCheckErrorKind.Network, inner: ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            int code = (int)response.StatusCode;
            // GitHub 무인증 rate limit: 403 + body의 "rate limit" 패턴 + X-RateLimit-Remaining: 0
            bool isRateLimit = code == 403 &&
                               (rawJson.Contains("rate limit", StringComparison.OrdinalIgnoreCase) ||
                                (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remVals) &&
                                 remVals.FirstOrDefault() == "0"));

            if (isRateLimit)
            {
                string? retryAt = null;
                // 우선순위: X-RateLimit-Reset (epoch) > Retry-After (delta seconds)
                if (response.Headers.TryGetValues("X-RateLimit-Reset", out var resetVals) &&
                    long.TryParse(resetVals.FirstOrDefault(), out var epoch))
                {
                    retryAt = DateTimeOffset.FromUnixTimeSeconds(epoch).ToLocalTime().ToString("HH:mm");
                }
                else if (response.Headers.TryGetValues("Retry-After", out var raVals) &&
                         int.TryParse(raVals.FirstOrDefault(), out var raSec))
                {
                    retryAt = DateTimeOffset.UtcNow.AddSeconds(raSec).ToLocalTime().ToString("HH:mm");
                }
                throw new UpdateCheckException(
                    "GitHub API rate limit exceeded (60/h, unauthenticated).",
                    UpdateCheckErrorKind.RateLimit,
                    statusCode: code,
                    retryAtLocal: retryAt);
            }

            throw new UpdateCheckException(
                $"GitHub API returned HTTP {code}.",
                UpdateCheckErrorKind.ApiError,
                statusCode: code);
        }

        using var doc = JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // 정상 응답이지만 message 필드가 있으면 (방어적) — 분류는 ApiError 로
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var msgEl))
            throw new UpdateCheckException(
                $"GitHub API: {msgEl.GetString()}",
                UpdateCheckErrorKind.ApiError);

        // Collect all releases newer than current version, sorted newest first
        var newer = new List<(Version ver, string tag, string body, JsonElement element)>();
        foreach (var rel in root.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (rel.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) continue;

            var tag = rel.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            var verStr = tag.TrimStart('v');
            if (!Version.TryParse(verStr, out var ver)) continue;
            if (ver <= CurrentVersion) continue;

            var body = rel.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            newer.Add((ver, tag, body, rel));
        }

        if (newer.Count == 0) return null;

        newer.Sort((a, b) => b.ver.CompareTo(a.ver)); // newest first
        var latest = newer[0];

        // Build combined release notes: each version gets a header
        var notesBuilder = new System.Text.StringBuilder();
        foreach (var (ver, tag, body, _) in newer)
        {
            var filtered = Loc.FilterReleaseNotes(body);
            if (string.IsNullOrWhiteSpace(filtered)) continue;
            if (notesBuilder.Length > 0) notesBuilder.AppendLine();
            notesBuilder.AppendLine($"## {tag}");
            notesBuilder.Append(filtered);
        }

        string? exeUrl = null;
        string? sha256Url = null;

        foreach (var asset in latest.element.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            var url  = asset.GetProperty("browser_download_url").GetString() ?? "";

            if (name.Equals("ClaudeUsageTray.exe", StringComparison.OrdinalIgnoreCase))
                exeUrl = url;
            else if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
                     name.Equals("SHA256.txt", StringComparison.OrdinalIgnoreCase))
                sha256Url = url;
        }

        if (exeUrl is null) return null;

        return new UpdateInfo(latest.ver, exeUrl,
            sha256Url ?? "", notesBuilder.ToString().TrimEnd());
    }

    /// <summary>
    /// Downloads the new executable to a temporary location and verifies its SHA256.
    /// Reports progress via the provided action.
    /// </summary>
    /// <param name="allowUnverified">
    /// 릴리스에 SHA256 자산이 없어도 설치를 진행할지. 기본값(false)은 거부다 —
    /// 카운트다운 무인 설치가 검증 불가능한 바이너리를 조용히 설치하는 일이 없어야 한다.
    /// 사용자가 경고를 보고 직접 실행을 누른 경우에만 호출부가 true 를 넘긴다.
    /// </param>
    public async Task<string> DownloadAndPrepareUpdateAsync(string downloadUrl, string sha256Url,
                                                            Action<int, string> onProgress,
                                                            bool allowUnverified = false)
    {
        // 내려받기 전에 막는다: 설치하지 못할 30MB 를 받을 이유가 없고, 검증 여부는 다운로드 결과와
        // 무관하게 릴리스 자산 구성만으로 이미 결정돼 있다.
        if (string.IsNullOrWhiteSpace(sha256Url) && !allowUnverified)
            throw new InvalidOperationException(
                "This release has no SHA256 checksum asset; refusing to install it unattended.");

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
                    onProgress(pc, Loc.DownloadingUpdate);
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
                File.Delete(tempExe);
                throw new Exception("SHA256 verification failed", ex);
            }
        }

        return tempExe;
    }

    /// <summary>
    /// exe 교체 스크립트를 생성한다.
    /// <para>
    /// 핵심 불변식: <b>어떤 실패 경로에서도 실행 파일이 사라지면 안 된다.</b> 이전 구현은 원본을
    /// <c>Remove-Item</c> 으로 지운 뒤 새 파일을 옮겼기 때문에, 삭제만 성공하고 이동이 실패하면
    /// (파일 잠금·권한·디스크 문제) 앱이 통째로 사라졌다. 그래서 원본은 지우지 않고 <c>.bak</c> 로
    /// 물러 두었다가, 교체가 끝까지 성공했을 때만 폐기하고 실패하면 되돌린 뒤 다시 실행한다.
    /// </para>
    /// 문자열 생성을 분리해 둔 이유는 테스트에서 실제 PowerShell 로 돌려 롤백을 검증하기 위해서다.
    /// </summary>
    internal static string BuildSwapScript(string oldExe, string newExe, string logPath, string ps1Path)
    {
        // Escape single quotes for PowerShell string literal
        string Esc(string? s) => (s ?? "").Replace("'", "''");

        // 프로세스 이름은 교체 대상 exe 에서 유도한다 — 이름을 하드코딩하면 테스트에서 실제 앱을 죽인다.
        var processName = Path.GetFileNameWithoutExtension(oldExe);

        // Robust PowerShell script (using regular verbatim string to avoid C# interpolation conflicts)
        return @"
$ErrorActionPreference = 'Stop'
$log = '{LOG_PATH}'
""Update started at $(Get-Date)"" | Out-File -LiteralPath $log

$oldExe  = '{OLD_EXE}'
$newExe  = '{NEW_EXE}'
$backup  = ""$oldExe.bak""
$procName = '{PROC_NAME}'

function Log($msg) {
    ""$(Get-Date -Format 'HH:mm:ss') - $msg"" | Out-File -LiteralPath $log -Append
}

# 교체에 실패했으면 물러 둔 원본을 제자리로 돌린다. 이걸 못 하면 앱이 사라진 상태로 남는다.
function Restore-Original {
    if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $oldExe)) {
        try {
            Move-Item -LiteralPath $backup -Destination $oldExe -Force -ErrorAction Stop
            Log ""Rolled back to the original executable.""
        } catch {
            Log ""ROLLBACK FAILED: $($_.Exception.Message). Original kept at $backup""
        }
    }
}

# 이미 떠 있으면(=강제 종료 실패) 중복 실행하지 않는다 — 단일 인스턴스 경고창이 뜬다.
function Start-App {
    if (Get-Process -Name $procName -ErrorAction SilentlyContinue) {
        Log ""Already running; skipping start.""
        return
    }
    if (-not (Test-Path -LiteralPath $oldExe)) {
        Log ""START SKIPPED: $oldExe is missing.""
        return
    }
    try {
        Start-Process -FilePath $oldExe
        Log ""Started $oldExe""
    } catch {
        Log ""START FAILED: $($_.Exception.Message)""
    }
}

try {
    # 1. Wait for process exit (Graceful)
    Log ""Waiting for process to exit...""
    $timeout = 20
    while ($timeout -gt 0) {
        $p = Get-Process -Name $procName -ErrorAction SilentlyContinue
        if (-not $p) { break }
        Start-Sleep -Seconds 1
        $timeout--
    }

    # 2. Force kill if still running
    $p = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($p) {
        Log ""Process still running. Force killing...""
        Stop-Process -Name $procName -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }

    # 3. 원본을 .bak 으로 물러 둔 뒤 교체한다 (잠금이 풀리기를 기다리며 재시도)
    Log ""Replacing executable...""
    $retry = 5
    $success = $false
    while ($retry -gt 0) {
        try {
            Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            if (Test-Path -LiteralPath $oldExe) {
                Move-Item -LiteralPath $oldExe -Destination $backup -Force -ErrorAction Stop
            }
            Move-Item -LiteralPath $newExe -Destination $oldExe -Force -ErrorAction Stop
            $success = $true
            Log ""Move successful.""
            break
        } catch {
            Log ""Move failed: $($_.Exception.Message). Retrying ($retry)...""
            Restore-Original
            $retry--
            Start-Sleep -Seconds 2
        }
    }

    if (-not $success) { throw ""Failed to replace executable after retries."" }

    # 4. Restart
    Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    Log ""Starting new version...""
    Start-App
    Log ""Update complete.""
}
catch {
    Log ""CRITICAL ERROR: $($_.Exception.Message)""

    # 실패 경로: 원본을 되돌리고 그대로 다시 띄운다. 업데이트를 못 했을지언정 앱은 남아야 한다.
    Restore-Original
    Remove-Item -LiteralPath $newExe -Force -ErrorAction SilentlyContinue
    Start-App
    Log ""Rolled back; the previous version is running.""
}
finally {
    # Self cleanup
    Remove-Item -LiteralPath '{PS1_PATH}' -Force -ErrorAction SilentlyContinue
}
"
        .Replace("{LOG_PATH}", Esc(logPath))
        .Replace("{OLD_EXE}", Esc(oldExe))
        .Replace("{NEW_EXE}", Esc(newExe))
        .Replace("{PROC_NAME}", Esc(processName))
        .Replace("{PS1_PATH}", Esc(ps1Path));
    }

    /// <summary>
    /// Launches a highly robust PowerShell script to swap the current EXE with the prepared one.
    /// Handles process termination, force-kill, path encoding, retries, and rollback on failure.
    /// </summary>
    public void ApplyPreparedUpdate(string preparedExePath)
    {
        var currentExe = Process.GetCurrentProcess().MainModule?.FileName
            ?? Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "ClaudeUsageTray.exe");

        var ps1Path = Path.Combine(Path.GetTempPath(), $"claude_swap_{Guid.NewGuid():N}.ps1");
        var logPath = Path.Combine(Path.GetTempPath(), "claude_update_debug.log");

        var psCommand = BuildSwapScript(currentExe, preparedExePath, logPath, ps1Path);

        try
        {
            // IMPORTANT: Use UTF8 with BOM so PowerShell 5.1 correctly reads Korean paths
            var encoding = new System.Text.UTF8Encoding(true);
            File.WriteAllText(ps1Path, psCommand, encoding);

            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ps1Path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex)
        {
            // 스왑 스크립트를 띄우지도 못했으면 종료하지 않는다 — 종료해 봐야 업데이트는 안 되고 앱만 사라진다.
            // 호출부(UpdateDialog)가 이 예외를 받아 오류를 표시하고 앱은 그대로 남는다.
            File.AppendAllText(logPath, $"Failed to launch PowerShell: {ex.Message}{Environment.NewLine}");
            throw new InvalidOperationException("Failed to launch the update installer", ex);
        }

        // Final shutdown — 여기부터는 스왑 스크립트가 이 프로세스의 종료를 기다리고 있다.
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
