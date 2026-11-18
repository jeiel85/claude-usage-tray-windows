using System.IO;
using System.Net;
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
/// </summary>
public class UpdateVerificationTests
{
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
