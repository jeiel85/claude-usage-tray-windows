using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaudeUsageTray.Services;
using ClaudeUsageTray.ViewModels;
using Xunit;

namespace ClaudeUsageTray.Tests.Services;

/// <summary>
/// 자동 업데이트는 서명(<see cref="UpdateSignature"/>, #290)을 확인한 exe 만 설치한다. 서명 없는 릴리스는 고르지 않고,
/// 서명이 맞지 않는 다운로드는 지운다. 검증 없이 설치하는 경로는 없다.
/// 오류 문구를 <c>Loc</c> 로 비교하므로, 언어를 바꾸는 테스트와 나란히 돌지 않게 같은 컬렉션에 둔다(#213).
/// </summary>
[Collection("WpfTests")]
public class UpdateVerificationTests
{
    // 확인 단계 진단 로그(#89): 찾음·최신·실패 사유를 한 줄로 남긴다.
    [Fact]
    public void DescribeCheckOutcome_NamesTheResultAndTheFailureKind()
    {
        var current = new Version(1, 72, 10);
        var info = new UpdateService.UpdateInfo(new Version(1, 73, 0), "https://x/ClaudeUsageTray.exe", "https://x/ClaudeUsageTray.exe.sig", "");

        Assert.Equal("check: v1.73.0 available (current 1.72.10)", UpdateService.DescribeCheckOutcome(info, null, current));
        Assert.StartsWith("check: up to date (current 1.72.10", UpdateService.DescribeCheckOutcome(null, null, current));

        var rateLimit = new UpdateCheckException("GitHub API rate limit exceeded.", UpdateCheckErrorKind.RateLimit, 403, "14:05");
        Assert.Equal("check failed: RateLimit HTTP 403 retry 14:05 — GitHub API rate limit exceeded. (current 1.72.10)",
            UpdateService.DescribeCheckOutcome(null, rateLimit, current));
        Assert.Equal("check failed: JsonException — bad (current 1.72.10)",
            UpdateService.DescribeCheckOutcome(null, new System.Text.Json.JsonException("bad"), current));
    }

    [Fact]
    public void AppendDebugLog_AddsTimestampedLines_AndStartsOverPastTheCap()
    {
        var path = Path.Combine(Path.GetTempPath(), $"update-log-{Guid.NewGuid():N}.log");
        try
        {
            UpdateService.AppendDebugLog(path, "first", maxBytes: 200);
            UpdateService.AppendDebugLog(path, "second", maxBytes: 200);
            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{2}:\d{2}\] first$", lines[0]);

            File.AppendAllText(path, new string('x', 300));
            UpdateService.AppendDebugLog(path, "after cap", maxBytes: 200);
            Assert.EndsWith("] after cap", File.ReadAllLines(path).Single());
        }
        finally
        {
            File.Delete(path);
        }
    }

    // 교체 스크립트는 PowerShell 5.1 Out-File 기본값(UTF-16 LE + BOM)으로 로그를 만든다 — 그 뒤에 붙인 줄도 같은 인코딩이어야
    // 읽힌다(v1.72.11 에서 UTF-8 로 붙여 깨져 보였다). 새 파일은 UTF-8 BOM 으로 시작한다.
    [Fact]
    public void AppendDebugLog_FollowsTheEncodingOfTheExistingFile()
    {
        var utf16 = Path.Combine(Path.GetTempPath(), $"update-log-{Guid.NewGuid():N}.log");
        var fresh = Path.Combine(Path.GetTempPath(), $"update-log-{Guid.NewGuid():N}.log");
        try
        {
            File.WriteAllText(utf16, "Update started at 2026-11-22" + Environment.NewLine, Encoding.Unicode);
            UpdateService.AppendDebugLog(utf16, "check: up to date (현재 1.72.12)", maxBytes: 1024 * 1024);
            var lines = File.ReadAllLines(utf16);
            Assert.Equal("Update started at 2026-11-22", lines[0]);
            Assert.EndsWith("] check: up to date (현재 1.72.12)", lines[1]);
            Assert.Equal(new byte[] { 0xFF, 0xFE }, File.ReadAllBytes(utf16).Take(2).ToArray());

            UpdateService.AppendDebugLog(fresh, "첫 줄", maxBytes: 1024 * 1024);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(fresh).Take(3).ToArray());
            Assert.EndsWith("] 첫 줄", File.ReadAllLines(fresh).Single());
        }
        finally
        {
            File.Delete(utf16);
            File.Delete(fresh);
        }
    }

    // 다른 쪽(교체 스크립트 등)이 로그를 쓰기로 열고 있어도, 앱 안에서 여러 확인이 겹쳐도 줄을 잃지 않는다(AGENTS.md: FileShare.ReadWrite).
    [Fact]
    public async Task AppendDebugLog_SharesTheFile_AndKeepsEveryLineFromOverlappingWriters()
    {
        var path = Path.Combine(Path.GetTempPath(), $"update-log-{Guid.NewGuid():N}.log");
        try
        {
            using (var other = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                UpdateService.AppendDebugLog(path, "while another writer is open", maxBytes: 1024 * 1024);
            }

            await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
                Task.Run(() => UpdateService.AppendDebugLog(path, $"line {i}", maxBytes: 1024 * 1024))));

            var lines = File.ReadAllLines(path);
            Assert.Contains(lines, l => l.EndsWith("] while another writer is open", StringComparison.Ordinal));
            Assert.Equal(21, lines.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // 대화상자 오류 문구(#89): 이미 현재 언어인 서비스 문구는 그대로, .NET 원문 예외는 "다운로드 실패" 로 감싼다.
    [Fact]
    public void DescribeInstallFailure_KeepsLocalizedMessages_AndWrapsRawOnes()
    {
        var previous = Loc.CurrentLang;
        try
        {
            Loc.SetLanguage("ko");
            Assert.Equal(Loc.UpdateSignatureInvalid,
                UpdateService.DescribeInstallFailure(new InvalidOperationException(Loc.UpdateSignatureInvalid)));
            Assert.Equal(Loc.UpdateInstallerLaunchFailed,
                UpdateService.DescribeInstallFailure(new InvalidOperationException(Loc.UpdateInstallerLaunchFailed)));
            Assert.Equal("업데이트 다운로드 실패: No such host is known.",
                UpdateService.DescribeInstallFailure(new HttpRequestException("No such host is known.")));
            // HttpClient 가 내는 InvalidOperationException 도 원문이다.
            Assert.Equal("업데이트 다운로드 실패: An invalid request URI was provided.",
                UpdateService.DescribeInstallFailure(new InvalidOperationException("An invalid request URI was provided.")));
        }
        finally
        {
            Loc.SetLanguage(previous);
        }
    }

    [Fact]
    public async Task Download_RefusesReleaseWithoutSignature_BeforeFetchingAnything()
    {
        var service = new UpdateService();
        var progressCalls = 0;

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DownloadAndPrepareUpdateAsync(
                "https://example.invalid/ClaudeUsageTray.exe",
                signatureUrl: "",
                new Version(9, 0, 0),
                onProgress: (_, _) => progressCalls++));

        Assert.Equal(Loc.UpdateSignatureInvalid, ex.Message);
        // onProgress 는 내려받기를 시작할 때 처음 불린다 — 0 이면 네트워크에 나가기 전에 막힌 것이다.
        Assert.Equal(0, progressCalls);
    }

    [Fact]
    public async Task Download_RejectsAndDeletesAnExeWhoseSignatureIsNotFromTheReleaseKey()
    {
        // 릴리스를 바꿀 수 있는 쪽이 exe 와 "서명" 을 함께 바꾼 경우: 자기 키로는 올바른 서명이어도 앱에 내장한 키로는 틀려야 한다.
        var exe = Encoding.ASCII.GetBytes("MZ not really an exe");
        var version = new Version(9, 0, 0);
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = Convert.ToBase64String(attackerKey.SignData(
            UpdateSignature.Message(version, Convert.ToHexString(SHA256.HashData(exe))), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        await using var server = await FileServer.StartAsync(new Dictionary<string, byte[]>
        {
            ["/ClaudeUsageTray.exe"] = exe,
            ["/ClaudeUsageTray.exe.sig"] = Encoding.ASCII.GetBytes(forged),
        });
        var before = TempUpdateFiles();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UpdateService().DownloadAndPrepareUpdateAsync(
                server.Url("/ClaudeUsageTray.exe"), server.Url("/ClaudeUsageTray.exe.sig"), version, (_, _) => { }));

        Assert.Equal(Loc.UpdateSignatureInvalid, ex.Message);
        Assert.Equal(2, server.Requests); // 서명과 exe 를 실제로 받은 뒤 거부했다
        Assert.Empty(TempUpdateFiles().Except(before)); // 받은 파일은 남지 않는다
    }

    [Fact]
    public void SelectUpdate_PicksTheNewestSignedRelease_AndSkipsUnsignedOnes()
    {
        // 1.70.0 은 서명 전(CI 가 막 올린 상태) — 건너뛰고 1.69.0 을 고른다. 노트는 1.69.0 까지의 새 버전만.
        var releases = Releases(
            Release("v1.70.0", sig: false, notes: "seventy"),
            Release("v1.69.0", sig: true, notes: "sixty-nine"),
            Release("v1.68.5", sig: false, notes: "sixty-eight-five"),
            Release("v1.68.0", sig: true, notes: "current"));

        var update = UpdateService.SelectUpdate(releases, new Version(1, 68, 0));

        Assert.NotNull(update);
        Assert.Equal(new Version(1, 69, 0), update!.version);
        Assert.EndsWith("/v1.69.0/ClaudeUsageTray.exe", update.downloadUrl);
        Assert.EndsWith("/v1.69.0/" + UpdateSignature.AssetName, update.signatureUrl);
        Assert.Contains("sixty-nine", update.releaseNotes);
        Assert.Contains("sixty-eight-five", update.releaseNotes);
        Assert.DoesNotContain("seventy", update.releaseNotes);
        Assert.DoesNotContain("current", update.releaseNotes);
    }

    [Fact]
    public void SelectUpdate_OffersNothing_WhenNoNewerReleaseIsSigned()
    {
        // 서명 이전 릴리스, 서명 없이 올라온 릴리스(탈취된 계정 등), 초안·사전 릴리스는 업데이트로 보지 않는다.
        var releases = Releases(
            Release("v2.0.0", sig: false),
            Release("v1.99.0", sig: true, draft: true),
            Release("v1.98.0", sig: true, prerelease: true),
            Release("v1.0.0", sig: true));

        Assert.Null(UpdateService.SelectUpdate(releases, new Version(1, 50, 0)));
    }

    // #300: 1.41.0 을 찾아 둔 뒤 1.42.0 이 나왔으면 받기 직전 재확인이 1.42.0 을 골라야 한다. 아니면 1.41.0 을 설치하고
    // 재시작 직후 시작 시 확인이 1.42.0 을 찾아 한 번 더 업데이트한다.
    [Fact]
    public async Task InstallTarget_MovesToTheNewerRelease_PublishedAfterTheOfferWasCached()
    {
        var current = new Version(1, 40, 0);
        var offered = UpdateService.SelectUpdate(Releases(Release("v1.41.0", sig: true, notes: "forty-one")), current)!;
        var latest = UpdateService.SelectUpdate(Releases(
            Release("v1.42.0", sig: true, notes: "forty-two"),
            Release("v1.41.0", sig: true, notes: "forty-one")), current);

        var target = await UpdateService.ResolveInstallTargetAsync(offered, () => Task.FromResult(latest));

        Assert.Equal(new Version(1, 42, 0), target.version);
        Assert.EndsWith("/v1.42.0/ClaudeUsageTray.exe", target.downloadUrl);
        Assert.EndsWith("/v1.42.0/" + UpdateSignature.AssetName, target.signatureUrl);
        Assert.Contains("forty-two", target.releaseNotes);
        Assert.Contains("forty-one", target.releaseNotes); // 건너뛰는 중간 버전의 변경도 노트에 남는다
    }

    // 새 버전이 아직 서명 전이면(CI 릴리스와 sign-release.ps1 사이) 찾아 둔 버전을 그대로 받는다 — 서명된 것만 설치한다(D-1).
    [Fact]
    public async Task InstallTarget_KeepsTheOffer_WhenTheNewerReleaseIsNotSignedYet()
    {
        var current = new Version(1, 40, 0);
        var offered = UpdateService.SelectUpdate(Releases(Release("v1.41.0", sig: true)), current)!;
        var latest = UpdateService.SelectUpdate(Releases(
            Release("v1.42.0", sig: false),
            Release("v1.41.0", sig: true)), current);

        var target = await UpdateService.ResolveInstallTargetAsync(offered, () => Task.FromResult(latest));

        Assert.Same(offered, target);
    }

    [Theory]
    [InlineData(null)]       // 다시 확인하니 받을 것이 없음(릴리스가 내려간 경우 등)
    [InlineData("1.41.0")]   // 같은 버전
    [InlineData("1.40.5")]   // 더 옛 버전
    public async Task InstallTarget_KeepsTheOffer_WhenTheRecheckFindsNothingNewer(string? recheckedVersion)
    {
        var offered = Info("1.41.0");
        var rechecked = recheckedVersion is null ? null : Info(recheckedVersion);

        var target = await UpdateService.ResolveInstallTargetAsync(offered, () => Task.FromResult(rechecked));

        Assert.Same(offered, target);
    }

    // 재확인 실패는 설치를 막지 않는다 — 찾아 둔 버전도 서명이 맞아야 설치되는 정상 릴리스다.
    public static TheoryData<Exception> RecheckFailures => new()
    {
        new UpdateCheckException("rate limited", UpdateCheckErrorKind.RateLimit, statusCode: 403),
        new UpdateCheckException("timeout", UpdateCheckErrorKind.Timeout),
        new HttpRequestException("offline"),
        new JsonException("broken body"),
    };

    [Theory]
    [MemberData(nameof(RecheckFailures))]
    public async Task InstallTarget_FallsBackToTheOffer_WhenTheRecheckFails(Exception failure)
    {
        var offered = Info("1.41.0");

        var target = await UpdateService.ResolveInstallTargetAsync(offered, () => Task.FromException<UpdateService.UpdateInfo?>(failure));

        Assert.Same(offered, target);
    }

    [Fact]
    public void AutoUpdatePlan_DisablesCountdown_WhenPreviousAutoAttemptFailed()
    {
        var (seconds, notice) = Plan(autoRetryExhausted: true);

        Assert.Equal(0, seconds);
        Assert.NotNull(notice);
    }

    // 사용자가 끈 경우엔 조용히 수동 모드로 — 본인이 고른 동작이라 경고를 띄우지 않는다.
    [Fact]
    public void AutoUpdatePlan_DisablesCountdownSilently_WhenUserTurnedAutoUpdateOff()
    {
        var (seconds, notice) = Plan(autoUpdateEnabled: false);

        Assert.Equal(0, seconds);
        Assert.Null(notice);
    }

    [Fact]
    public void AutoUpdatePlan_UsesConfiguredCountdown_WhenEverythingIsFine()
    {
        var (seconds, notice) = Plan(countdownSeconds: 25);

        Assert.Equal(25, seconds);
        Assert.Null(notice);
    }

    [Theory]
    [InlineData(0, 60)]      // 미설정(구버전 설정 파일) → 기본값
    [InlineData(-5, 60)]     // 잘못된 값 → 기본값
    [InlineData(3, 10)]      // 하한 미만 → 하한
    [InlineData(9999, 300)]  // 상한 초과 → 상한
    [InlineData(45, 45)]     // 범위 내 → 그대로
    public void AutoUpdateCountdown_IsClampedToUsableRange(int stored, int expected)
    {
        Assert.Equal(expected, MainViewModel.ClampAutoUpdateCountdown(stored));
    }

    private static (int seconds, string? notice) Plan(
        bool autoRetryExhausted = false, bool autoUpdateEnabled = true, int countdownSeconds = 60) =>
        MainViewModel.ResolveAutoUpdatePlan(autoRetryExhausted, autoUpdateEnabled, countdownSeconds);

    private static UpdateService.UpdateInfo Info(string version) => new(
        Version.Parse(version),
        $"https://example.invalid/v{version}/ClaudeUsageTray.exe",
        $"https://example.invalid/v{version}/{UpdateSignature.AssetName}",
        $"## v{version}");

    private static HashSet<string> TempUpdateFiles() =>
        Directory.GetFiles(Path.GetTempPath(), "ClaudeUsageTray_new_*.exe").ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Release(string tag, bool sig, string notes = "", bool draft = false, bool prerelease = false)
    {
        var assets = new List<object>
        {
            new { name = "ClaudeUsageTray.exe", browser_download_url = $"https://example.invalid/{tag}/ClaudeUsageTray.exe" },
            new { name = "SHA256.txt", browser_download_url = $"https://example.invalid/{tag}/SHA256.txt" },
        };
        if (sig) assets.Add(new { name = UpdateSignature.AssetName, browser_download_url = $"https://example.invalid/{tag}/{UpdateSignature.AssetName}" });
        return JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, body = notes, assets });
    }

    private static JsonElement Releases(params string[] releases) =>
        JsonDocument.Parse("[" + string.Join(",", releases) + "]").RootElement.Clone();

    /// <summary>경로별 고정 바이트를 돌려주는 루프백 HTTP 서버(연결당 요청 하나).</summary>
    private sealed class FileServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private int _requests;

        private FileServer(IReadOnlyDictionary<string, byte[]> files)
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Task.Run(() => ServeAsync(files));
        }

        public int Requests => Volatile.Read(ref _requests);

        public static Task<FileServer> StartAsync(IReadOnlyDictionary<string, byte[]> files) => Task.FromResult(new FileServer(files));

        public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

        private async Task ServeAsync(IReadOnlyDictionary<string, byte[]> files)
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
                using (client)
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync() ?? "";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                    Interlocked.Increment(ref _requests);
                    var path = requestLine.Split(' ') is { Length: >= 2 } parts ? parts[1] : "";
                    var found = files.TryGetValue(path, out var body);
                    body ??= [];
                    var head = $"HTTP/1.1 {(found ? "200 OK" : "404 Not Found")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
                    await stream.WriteAsync(body);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            await _loop;
            _cts.Dispose();
        }
    }
}
