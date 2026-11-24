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

    /// <summary>업데이트 진단 로그. 교체 스크립트도 같은 파일에 쓴다.</summary>
    internal static readonly string DebugLogPath = Path.Combine(Path.GetTempPath(), "claude_update_debug.log");

    /// <summary>진단 로그가 이보다 커지면 지우고 새로 쓴다 — 확인은 하루 몇 번이라 평소에는 닿지 않는다.</summary>
    internal const long DebugLogMaxBytes = 512 * 1024;

    /// <summary>
    /// Returns UpdateInfo if a newer release exists, null if already up to date.
    /// Throws <see cref="UpdateCheckException"/> on classified failures so callers can route messaging by Kind.
    /// Release notes include all versions between current and latest.
    /// 결과(찾음·최신·실패 사유)를 진단 로그에 한 줄 남긴다(#89) — 화면에는 마지막 실패 하나만 툴팁으로 남아 원격 진단이 어렵다.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var info = await CheckForUpdateCoreAsync();
            AppendDebugLog(DebugLogPath, DescribeCheckOutcome(info, null, CurrentVersion), DebugLogMaxBytes);
            return info;
        }
        catch (Exception ex) when (ex is UpdateCheckException or JsonException)
        {
            AppendDebugLog(DebugLogPath, DescribeCheckOutcome(null, ex, CurrentVersion), DebugLogMaxBytes);
            throw;
        }
    }

    /// <summary>업데이트 확인 결과 한 줄(시각은 <see cref="AppendDebugLog"/> 가 붙인다).</summary>
    internal static string DescribeCheckOutcome(UpdateInfo? info, Exception? error, Version current) => error switch
    {
        UpdateCheckException ex => $"check failed: {ex.Kind}{(ex.StatusCode is { } code ? $" HTTP {code}" : "")}" +
                                   $"{(ex.RetryAtLocal is { } at ? $" retry {at}" : "")} — {ex.Message} (current {current})",
        not null => $"check failed: {error.GetType().Name} — {error.Message} (current {current})",
        _ when info is not null => $"check: v{info.version} available (current {current})",
        _ => $"check: up to date (current {current}; releases without a signature are skipped)",
    };

    private static readonly object DebugLogGate = new();

    /// <summary>파일 앞머리 BOM 으로 인코딩을 고른다. 비었거나 BOM 이 없으면 UTF-8(새 파일에는 BOM 을 붙인다).</summary>
    private static System.Text.Encoding DetectLogEncoding(Stream stream)
    {
        Span<byte> head = stackalloc byte[2];
        stream.Seek(0, SeekOrigin.Begin);
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        if (read == 2 && head[0] == 0xFF && head[1] == 0xFE) return System.Text.Encoding.Unicode;
        if (read == 2 && head[0] == 0xFE && head[1] == 0xFF) return System.Text.Encoding.BigEndianUnicode;
        return new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    }

    /// <summary>
    /// 진단 로그에 시각을 붙여 한 줄 쓴다. 파일이 <paramref name="maxBytes"/> 를 넘으면 지우고 새로 쓴다.
    /// 수동 확인과 주기 확인이 겹칠 수 있어 앱 안에서는 잠금으로 한 줄씩 쓰고, 교체 스크립트(다른 프로세스)와는
    /// <see cref="FileShare.ReadWrite"/> 로 함께 연다. 지우기는 다른 쪽이 열고 있으면 실패하는데, 그때는 이번에 지우지 않고
    /// 줄만 붙인다. 로그는 진단 보조라 쓰기 실패(잠김·권한)는 업데이트 동작에 영향을 주지 않게 건너뛴다.
    /// 인코딩은 파일을 따른다: 교체 스크립트는 PowerShell 5.1 <c>Out-File</c> 기본값(UTF-16 LE, BOM ff fe)으로 파일을 새로 만들므로
    /// 그 파일에 UTF-8 을 붙이면 그 줄이 깨져 보인다(v1.72.11 실측). 앞머리 BOM 을 보고 같은 인코딩으로 붙이고, 새 파일은 UTF-8 BOM 으로 만든다.
    /// </summary>
    internal static void AppendDebugLog(string path, string line, long maxBytes)
    {
        var text = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {line}{Environment.NewLine}";
        lock (DebugLogGate)
        {
            try
            {
                var file = new FileInfo(path);
                if (file.Exists && file.Length > maxBytes) file.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GC.KeepAlive(ex);
            }

            try
            {
                using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                var encoding = DetectLogEncoding(stream);
                stream.Seek(0, SeekOrigin.End);
                if (stream.Length == 0) stream.Write(encoding.GetPreamble());
                stream.Write(encoding.GetBytes(text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GC.KeepAlive(ex);
            }
        }
    }

    private async Task<UpdateInfo?> CheckForUpdateCoreAsync()
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
                    if (url.Length == 0 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                        continue;
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
    /// Input : 대화상자에 띄운 업데이트, 지금 다시 확인하는 함수(보통 <see cref="CheckForUpdateAsync"/>)
    /// Output: 실제로 받을 업데이트
    /// 핵심 로직: 확인 결과는 시작 시·24시간마다만 새로 고치므로, 찾아 둔 뒤 새 버전이 또 나왔는데 찾아 둔 것을 그대로 받으면 중간 버전을
    ///           설치하고 재시작 직후 한 번 더 업데이트하게 된다(#300). 그래서 받기 직전에 다시 확인해 더 새 버전이면 그걸 고른다. 다시
    ///           확인이 실패하면(네트워크·rate limit 등) 찾아 둔 것으로 진행한다 — 그것도 서명이 맞아야 설치되는 정상 릴리스라 확인 실패가
    ///           설치를 막을 이유는 없다.
    /// </summary>
    internal static async Task<UpdateInfo> ResolveInstallTargetAsync(UpdateInfo offered, Func<Task<UpdateInfo?>> checkLatest)
    {
        UpdateInfo? latest;
        try
        {
            latest = await checkLatest();
        }
        catch (Exception)
        {
            return offered;
        }
        return latest is not null && latest.version > offered.version ? latest : offered;
    }

    /// <summary>
    /// Input : 업데이트 받기·설치 중에 난 예외
    /// Output: 대화상자에 띄울 문구
    /// 핵심 로직: 이 서비스가 직접 내는 실패(서명 불일치·설치 프로그램 실행 실패)는 이미 현재 언어 문구라 그대로 쓴다.
    ///           그 밖의 예외(네트워크·디스크 등)는 .NET 원문이라 "다운로드 실패: …" 로 감싸 무엇이 실패했는지는 현재 언어로
    ///           알려 준다(#89). 예외 형식이 아니라 문구로 가린다 — HttpClient 도 <see cref="InvalidOperationException"/> 을 낸다.
    /// </summary>
    internal static string DescribeInstallFailure(Exception ex) =>
        ex.Message == Loc.UpdateSignatureInvalid || ex.Message == Loc.UpdateInstallerLaunchFailed
            ? ex.Message
            : Loc.UpdateDownloadFailed(ex.Message);

    /// <summary>업데이트 서명·exe 다운로드 크기 상한(#340).</summary>
    internal const int MaxSignatureBytes = 4 * 1024;
    internal const long MaxExeBytes = 200L * 1024 * 1024;

    private static async Task<string> ReadBoundedStringAsync(string url, int maxBytes)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } len && len > maxBytes)
            throw new InvalidOperationException($"Response exceeds {maxBytes} bytes.");

        using var stream = await response.Content.ReadAsStreamAsync();
        using var ms = new MemoryStream();
        var buffer = new byte[Math.Min(4096, maxBytes)];
        int read;
        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            if (ms.Length + read > maxBytes)
                throw new InvalidOperationException($"Response exceeds {maxBytes} bytes.");
            ms.Write(buffer, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
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
        var signature = await ReadBoundedStringAsync(signatureUrl, MaxSignatureBytes);

        var tempExe = Path.Combine(Path.GetTempPath(), $"ClaudeUsageTray_new_{Guid.NewGuid():N}.exe");
        try
        {
            using (var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                if (totalBytes > MaxExeBytes)
                    throw new InvalidOperationException($"Update package exceeds {MaxExeBytes} bytes.");

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(tempExe, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int read;
                while ((read = await contentStream.ReadAsync(buffer)) > 0)
                {
                    totalRead += read;
                    if (totalRead > MaxExeBytes)
                        throw new InvalidOperationException($"Update package exceeds {MaxExeBytes} bytes.");
                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    if (totalBytes > 0)
                        onProgress((int)((totalRead * 100) / totalBytes), Loc.DownloadingUpdate);
                }
            }

            onProgress(100, Loc.UpdateVerifying);
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
        // Escape ASCII and Unicode curly single quotes (U+2018~U+201B) for PowerShell single-quoted literals (#340)
        static string Esc(string? s)
        {
            var value = s ?? "";
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (ch is '\'' or '\u2018' or '\u2019' or '\u201A' or '\u201B') sb.Append(ch);
                sb.Append(ch);
            }
            return sb.ToString();
        }

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
        var logPath = DebugLogPath;

        var psCommand = BuildSwapScript(currentExe, preparedExePath, logPath, ps1Path);

        try
        {
            // IMPORTANT: Use UTF8 with BOM so PowerShell 5.1 correctly reads Korean paths
            var encoding = new System.Text.UTF8Encoding(true);
            File.WriteAllText(ps1Path, psCommand, encoding);

            Process.Start(new ProcessStartInfo(Mobile.MobileFirewall.SystemBinaryPath(@"WindowsPowerShell\v1.0\powershell.exe"))
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
            AppendDebugLog(logPath, $"Failed to launch PowerShell: {ex.Message}", DebugLogMaxBytes);
            throw new InvalidOperationException(Loc.UpdateInstallerLaunchFailed, ex);
        }

        // Final shutdown — 여기부터는 스왑 스크립트가 이 프로세스의 종료를 기다리고 있다.
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
            System.Windows.Application.Current.Shutdown());
    }
}
