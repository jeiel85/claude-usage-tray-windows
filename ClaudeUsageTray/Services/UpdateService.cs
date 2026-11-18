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
    /// <summary>설치할 업데이트. <paramref name="signatureUrl"/> 는 <see cref="UpdateSignature.AssetName"/> 자산 주소.</summary>
    public record UpdateInfo(Version version, string downloadUrl, string signatureUrl, string releaseNotes);

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

        return SelectUpdate(root, CurrentVersion);
    }

    /// <summary>
    /// Input : GitHub releases 목록 JSON, 지금 버전
    /// Output: 설치할 업데이트(없으면 null)
    /// 핵심 로직: exe 와 서명(<see cref="UpdateSignature.AssetName"/>)이 모두 있는 가장 새 릴리스를 고른다(#290). 서명이 없는 릴리스는
    ///           건너뛴다 — 서명은 CI 가 릴리스를 만든 뒤 배포 PC 에서 따로 올리므로 그 사이에는 없는 것이 정상이고, 탈취된 계정이
    ///           올린 릴리스에도 없다. 릴리스 노트는 고른 버전까지의 새 버전을 모두 싣는다(건너뛴 중간 버전의 변경도 그 빌드에 들어 있다).
    /// </summary>
    internal static UpdateInfo? SelectUpdate(JsonElement releases, Version current)
    {
        if (releases.ValueKind != JsonValueKind.Array) return null;
        var newer = new List<(Version Ver, string Tag, string Body, string? Exe, string? Sig)>();
        foreach (var rel in releases.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
            if (rel.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) continue;

            var tag = rel.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v'), out var ver)) continue;
            if (ver <= current) continue;

            string? exe = null, sig = null;
            if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                    if (url.Length == 0) continue;
                    if (name.Equals("ClaudeUsageTray.exe", StringComparison.OrdinalIgnoreCase)) exe = url;
                    else if (name.Equals(UpdateSignature.AssetName, StringComparison.OrdinalIgnoreCase)) sig = url;
                }
            }
            var body = rel.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            newer.Add((ver, tag, body, exe, sig));
        }

        newer.Sort((a, b) => b.Ver.CompareTo(a.Ver)); // newest first
        var chosen = newer.FirstOrDefault(r => r.Exe is not null && r.Sig is not null);
        if (chosen.Exe is null) return null;

        // Build combined release notes: each version gets a header
        var notesBuilder = new System.Text.StringBuilder();
        foreach (var (ver, tag, body, _, _) in newer)
        {
            if (ver > chosen.Ver) continue;
            var filtered = Loc.FilterReleaseNotes(body);
            if (string.IsNullOrWhiteSpace(filtered)) continue;
            if (notesBuilder.Length > 0) notesBuilder.AppendLine();
            notesBuilder.AppendLine($"## {tag}");
            notesBuilder.Append(filtered);
        }

        return new UpdateInfo(chosen.Ver, chosen.Exe, chosen.Sig!, notesBuilder.ToString().TrimEnd());
    }

    /// <summary>
    /// 새 exe 를 임시 폴더에 받고 업데이트 서명(<see cref="UpdateSignature"/>)을 확인한다. 서명이 맞지 않으면 받은 파일을 지우고 예외를 낸다.
    /// 서명은 <c>버전 + SHA-256</c> 에 대한 것이라 SHA256.txt 는 쓰지 않는다(그 파일은 서명 이전 버전과 손으로 받는 사람을 위해 남겨 둔다).
    /// 검증 없이 설치하는 경로는 없다 — 사용자가 직접 누른 경우도 마찬가지다(#290).
    /// </summary>
    public async Task<string> DownloadAndPrepareUpdateAsync(string downloadUrl, string signatureUrl, Version version,
                                                            Action<int, string> onProgress)
    {
        // 서명이 없으면 받지도 않는다 — 확인할 방법이 없는 30MB 를 받을 이유가 없다.
        if (string.IsNullOrWhiteSpace(signatureUrl))
            throw new InvalidOperationException(Loc.UpdateSignatureInvalid);

        onProgress(0, Loc.CheckingUpdate);
        var signature = await Http.GetStringAsync(signatureUrl);

        var tempExe = Path.Combine(Path.GetTempPath(), $"ClaudeUsageTray_new_{Guid.NewGuid():N}.exe");
        try
        {
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
                        onProgress((int)((totalRead * 100) / totalBytes), Loc.DownloadingUpdate);
                }
            }

            onProgress(100, "Verifying...");
            string actualHash;
            using (var fs = new FileStream(tempExe, FileMode.Open, FileAccess.Read, FileShare.Read))
                actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fs)).ToLowerInvariant();

            if (!UpdateSignature.Verify(version, actualHash, signature))
                throw new InvalidOperationException(Loc.UpdateSignatureInvalid);
            return tempExe;
        }
        catch
        {
            try { File.Delete(tempExe); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
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
}
