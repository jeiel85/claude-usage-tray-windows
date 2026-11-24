using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace ClaudeUsageTray.Tests.Tools;

/// <summary>
/// <c>tools/publish-play.ps1</c> 를 실제 PowerShell 7(<c>pwsh</c>) 프로세스로 dot-source 하여
/// CHANGELOG 추출·RS256 JWT 서명·트랙 JSON 생성·AAB 구조 검사·Play API 5단계 파이프라인(목 서버)을 검증한다.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PublishPlayScriptTests : IDisposable
{
    private readonly string _dir;
    private readonly string _scriptPath;

    public PublishPlayScriptTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"playpub_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _scriptPath = FindRepoFile(Path.Combine("tools", "publish-play.ps1"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private static string FindRepoFile(string relativePath)
    {
        var root = Environment.GetEnvironmentVariable("CUT_REPO_ROOT") is { Length: > 0 } env
            ? new DirectoryInfo(env)
            : new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "claude-usage-tray-windows.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        var full = Path.Combine(root!.FullName, relativePath);
        Assert.True(File.Exists(full), $"Expected repo file at {full}");
        return full;
    }

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    private (int ExitCode, string StdOut, string StdErr) RunPwsh(string body)
    {
        var fullScript = $"""
            $ErrorActionPreference = 'Stop'
            $OutputEncoding = [Text.Encoding]::UTF8
            [Console]::OutputEncoding = [Text.Encoding]::UTF8
            . {PsQuote(_scriptPath)}
            {body}
            """;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(fullScript));
        using var proc = Process.Start(new ProcessStartInfo("pwsh")
        {
            Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encoded}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        Assert.True(proc.WaitForExit(60_000), "pwsh script timed out after 60s");
        return (proc.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult());
    }

    private string WriteMinimalAab(string fileName, bool includeRequiredEntries = true, byte[]? extraPayload = null)
    {
        var path = Path.Combine(_dir, fileName);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        if (includeRequiredEntries)
        {
            using (var s = zip.CreateEntry("BundleConfig.pb").Open())
                s.Write(extraPayload ?? [1, 2, 3, 4]);
            using (var s = zip.CreateEntry("base/manifest/AndroidManifest.xml").Open())
                s.Write([5, 6, 7, 8]);
        }
        else
        {
            using var s = zip.CreateEntry("README.txt").Open();
            s.Write(Encoding.UTF8.GetBytes("not an aab"));
        }
        return path;
    }

    private static string CreateServiceAccountJson(RSA rsa, string clientEmail = "play-ci@demo.iam.gserviceaccount.com", string keyId = "kid-1")
    {
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            project_id = "demo-project",
            private_key_id = keyId,
            private_key = pem,
            client_email = clientEmail,
            token_uri = "https://oauth2.googleapis.com/token",
        });
    }

    [Fact]
    public void ExtractChangelogNotes_ParsesKoEnAndCapsAt500Chars()
    {
        var changelogPath = Path.Combine(_dir, "CHANGELOG.md");
        var longDetailKo = new string('가', 580);
        var longDetailEn = new string('A', 620);
        var content = $$"""
            # Changelog

            ## [1.73.0] - 2026-11-24

            <!-- ko -->
            ### 개선
            - **Antigravity 표시 개선 (#345)**: `ClientUserAgent` 및 [문서](https://example.com) 정리
            <!-- /ko -->

            <!-- en -->
            ### Improved
            - **Antigravity display (#345)**: updated `ClientUserAgent` and [docs](https://example.com)
            <!-- /en -->

            ## [1.72.9] - 2026-11-23

            <!-- ko -->
            - {{longDetailKo}}
            <!-- /ko -->

            <!-- en -->
            - {{longDetailEn}}
            <!-- /en -->
            """;
        File.WriteAllText(changelogPath, content, new UTF8Encoding(false));

        var (exit, stdout, stderr) = RunPwsh($$"""
            $n1 = Get-PlayChangelogNotes -ChangelogPath {{PsQuote(changelogPath)}} -VersionText '1.73.0'
            $n2 = Get-PlayChangelogNotes -ChangelogPath {{PsQuote(changelogPath)}} -VersionText '1.72.9'
            $n3 = Get-PlayChangelogNotes -ChangelogPath {{PsQuote(changelogPath)}} -VersionText '9.9.9'
            [pscustomobject]@{ n1 = $n1; n2 = $n2; n3 = $n3 } | ConvertTo-Json -Depth 5 -Compress
            """);
        Assert.True(exit == 0, $"stderr={stderr}");

        using var doc = JsonDocument.Parse(stdout.Trim());
        var n1Ko = doc.RootElement.GetProperty("n1").GetProperty("Ko").GetString()!;
        var n1En = doc.RootElement.GetProperty("n1").GetProperty("En").GetString()!;
        Assert.DoesNotContain("###", n1Ko);
        Assert.DoesNotContain("**", n1Ko);
        Assert.DoesNotContain("`", n1Ko);
        Assert.DoesNotContain("https://example.com", n1Ko);
        Assert.Contains("Antigravity 표시 개선 (#345): ClientUserAgent 및 문서 정리", n1Ko);
        Assert.Contains("Antigravity display (#345): updated ClientUserAgent and docs", n1En);

        var n2Ko = doc.RootElement.GetProperty("n2").GetProperty("Ko").GetString()!;
        var n2En = doc.RootElement.GetProperty("n2").GetProperty("En").GetString()!;
        Assert.Equal(500, n2Ko.Length);
        Assert.EndsWith("…", n2Ko);
        Assert.Equal(500, n2En.Length);
        Assert.EndsWith("…", n2En);

        Assert.Equal("v9.9.9 업데이트", doc.RootElement.GetProperty("n3").GetProperty("Ko").GetString());
        Assert.Equal("Release v9.9.9", doc.RootElement.GetProperty("n3").GetProperty("En").GetString());
    }

    [Fact]
    public void CreateServiceAccountJwt_ProducesCryptographicallyValidRs256Token()
    {
        using var rsa = RSA.Create(2048);
        var saPath = Path.Combine(_dir, "sa.json");
        File.WriteAllText(saPath, CreateServiceAccountJson(rsa), new UTF8Encoding(false));

        var (exit, stdout, stderr) = RunPwsh($"""
            $sa = (Get-Content -LiteralPath {PsQuote(saPath)} -Raw) | ConvertFrom-Json
            New-PlayServiceAccountJwt -ServiceAccount $sa -NowUnix 1795490000
            """);
        Assert.True(exit == 0, $"stderr={stderr}");

        var jwt = stdout.Trim();
        var parts = jwt.Split('.');
        Assert.Equal(3, parts.Length);

        var header = JsonDocument.Parse(Base64UrlDecode(parts[0])).RootElement;
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        Assert.Equal("kid-1", header.GetProperty("kid").GetString());

        var claims = JsonDocument.Parse(Base64UrlDecode(parts[1])).RootElement;
        Assert.Equal("play-ci@demo.iam.gserviceaccount.com", claims.GetProperty("iss").GetString());
        Assert.Equal("https://www.googleapis.com/auth/androidpublisher", claims.GetProperty("scope").GetString());
        Assert.Equal("https://oauth2.googleapis.com/token", claims.GetProperty("aud").GetString());
        Assert.Equal(1795490000L, claims.GetProperty("iat").GetInt64());
        Assert.Equal(1795493600L, claims.GetProperty("exp").GetInt64());

        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var sig = Base64UrlDecode(parts[2]);
        Assert.True(rsa.VerifyData(signingInput, sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void BuildTrackPayload_ValidatesStatusAndUserFraction()
    {
        var (exit, stdout, stderr) = RunPwsh("""
            $p1 = New-PlayTrackReleasePayload -Track 'alpha' -VersionCode 17301 -ReleaseName '1.73.1' -Status 'completed' -NotesKo '한국어 노트' -NotesEn 'English note'
            $p2 = New-PlayTrackReleasePayload -Track 'production' -VersionCode 17301 -ReleaseName '1.73.1' -Status 'inProgress' -UserFraction 0.25 -NotesKo '단계 배포'
            $p3 = New-PlayTrackReleasePayload -Track 'internal' -VersionCode 17301 -ReleaseName '1.73.1' -Status 'draft'
            [pscustomobject]@{ p1 = $p1; p2 = $p2; p3 = $p3 } | ConvertTo-Json -Compress
            """);
        Assert.True(exit == 0, $"stderr={stderr}");

        using var outer = JsonDocument.Parse(stdout.Trim());

        using var p1 = JsonDocument.Parse(outer.RootElement.GetProperty("p1").GetString()!);
        Assert.Equal("alpha", p1.RootElement.GetProperty("track").GetString());
        var r1 = p1.RootElement.GetProperty("releases")[0];
        Assert.Equal("completed", r1.GetProperty("status").GetString());
        Assert.Equal("17301", r1.GetProperty("versionCodes")[0].GetString());
        Assert.False(r1.TryGetProperty("userFraction", out _));
        Assert.Equal(2, r1.GetProperty("releaseNotes").GetArrayLength());

        using var p2 = JsonDocument.Parse(outer.RootElement.GetProperty("p2").GetString()!);
        var r2 = p2.RootElement.GetProperty("releases")[0];
        Assert.Equal("inProgress", r2.GetProperty("status").GetString());
        Assert.Equal(0.25, r2.GetProperty("userFraction").GetDouble(), 3);
        Assert.Equal(1, r2.GetProperty("releaseNotes").GetArrayLength());

        using var p3 = JsonDocument.Parse(outer.RootElement.GetProperty("p3").GetString()!);
        Assert.Equal("draft", p3.RootElement.GetProperty("releases")[0].GetProperty("status").GetString());

        var badInProgress = RunPwsh("New-PlayTrackReleasePayload -Track 'alpha' -VersionCode 17301 -ReleaseName '1.73.1' -Status 'inProgress' -UserFraction 0");
        Assert.NotEqual(0, badInProgress.ExitCode);

        var badCompleted = RunPwsh("New-PlayTrackReleasePayload -Track 'alpha' -VersionCode 17301 -ReleaseName '1.73.1' -Status 'completed' -UserFraction 0.5");
        Assert.NotEqual(0, badCompleted.ExitCode);
    }

    [Fact]
    public void ValidateAabArchive_ChecksRequiredEntries()
    {
        var validAab = WriteMinimalAab("valid.aab", includeRequiredEntries: true);
        var invalidAab = WriteMinimalAab("invalid.aab", includeRequiredEntries: false);

        var (okExit, okOut, okErr) = RunPwsh($"""
            Test-PlayAabArchive -AabPath {PsQuote(validAab)} -SkipCertCheck | ConvertTo-Json -Compress
            """);
        Assert.True(okExit == 0, $"stderr={okErr}");
        using var parsed = JsonDocument.Parse(okOut.Trim());
        Assert.True(parsed.RootElement.GetProperty("SizeBytes").GetInt64() > 0);

        var (badExit, _, badErr) = RunPwsh($"Test-PlayAabArchive -AabPath {PsQuote(invalidAab)} -SkipCertCheck");
        Assert.NotEqual(0, badExit);
        Assert.Contains("BundleConfig.pb", badErr);
    }

    [Fact]
    public async Task UploadPipeline_CompletesAllFivePlayApiStepsAgainstMockServer()
    {
        using var rsa = RSA.Create(2048);
        var saPath = Path.Combine(_dir, "sa.json");
        File.WriteAllText(saPath, CreateServiceAccountJson(rsa), new UTF8Encoding(false));

        var aabBytes = File.ReadAllBytes(WriteMinimalAab("app-play-release.aab", extraPayload: Encoding.UTF8.GetBytes("FAKE-AAB-PAYLOAD")));
        var aabPath = Path.Combine(_dir, "app-play-release.aab");

        await using var server = new MockPlayServer(req =>
        {
            return req.Path switch
            {
                "/oauth2/token" when req.Method == "POST" =>
                    (200, """{"access_token":"tok-secret-123","token_type":"Bearer","expires_in":3600}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits" when req.Method == "POST" =>
                    (200, """{"id":"edit-42","expiryTimeSeconds":"9999999999"}"""),
                "/upload/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-42/bundles?uploadType=media" when req.Method == "POST" =>
                    (200, """{"versionCode":17300,"sha256":"deadbeef"}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-42/tracks/alpha" when req.Method == "PUT" =>
                    (200, """{"track":"alpha"}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-42:commit?changesNotSentForReview=true" when req.Method == "POST" =>
                    (200, """{"id":"edit-42"}"""),
                _ => (404, """{"error":{"message":"unexpected path"}}"""),
            };
        });

        var (exit, stdout, stderr) = RunPwsh($"""
            $sa = (Get-Content -LiteralPath {PsQuote(saPath)} -Raw) | ConvertFrom-Json
            $res = Invoke-PlayUploadPipeline `
                -ServiceAccount $sa `
                -PackageName 'com.jeiel85.aiusage' `
                -AabPath {PsQuote(aabPath)} `
                -ExpectedVersionCode 17300 `
                -ReleaseName '1.73.0' `
                -Track 'alpha' `
                -Status 'completed' `
                -NotesKo '한국어 노트' `
                -NotesEn 'English note' `
                -ChangesNotSentForReview `
                -TokenUri {PsQuote(server.Url("/oauth2/token"))} `
                -ApiBaseUri {PsQuote(server.BaseUri)} `
                -AllowNonGoogleEndpointsForTest
            $res | ConvertTo-Json -Compress
            """);
        Assert.True(exit == 0, $"stderr={stderr}");

        Assert.Equal(5, server.Requests.Count);
        Assert.Equal(
            new[]
            {
                "POST /oauth2/token",
                "POST /androidpublisher/v3/applications/com.jeiel85.aiusage/edits",
                "POST /upload/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-42/bundles?uploadType=media",
                "PUT /androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-42/tracks/alpha",
                "POST /androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-42:commit?changesNotSentForReview=true",
            },
            server.Requests.Select(r => $"{r.Method} {r.Path}").ToArray());

        for (var i = 1; i < server.Requests.Count; i++)
            Assert.Equal("Bearer tok-secret-123", server.Requests[i].Authorization);

        Assert.Contains("assertion=", Encoding.UTF8.GetString(server.Requests[0].Body));
        Assert.Equal(aabBytes, server.Requests[2].Body);

        using var trackDoc = JsonDocument.Parse(server.Requests[3].Body);
        Assert.Equal("alpha", trackDoc.RootElement.GetProperty("track").GetString());
        Assert.Equal("17300", trackDoc.RootElement.GetProperty("releases")[0].GetProperty("versionCodes")[0].GetString());
        Assert.DoesNotContain("tok-secret-123", stdout);

        using var resDoc = JsonDocument.Parse(stdout.Trim());
        Assert.Equal("edit-42", resDoc.RootElement.GetProperty("CommittedId").GetString());
    }

    [Fact]
    public async Task UploadPipeline_DeletesEditAndShowsHintOnPlayError()
    {
        using var rsa = RSA.Create(2048);
        var saPath = Path.Combine(_dir, "sa.json");
        File.WriteAllText(saPath, CreateServiceAccountJson(rsa), new UTF8Encoding(false));
        var aabPath = WriteMinimalAab("app-play-release.aab");

        await using var server = new MockPlayServer(req =>
        {
            return req.Path switch
            {
                "/oauth2/token" when req.Method == "POST" =>
                    (200, """{"access_token":"tok-1","token_type":"Bearer"}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits" when req.Method == "POST" =>
                    (200, """{"id":"edit-err"}"""),
                "/upload/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-err/bundles?uploadType=media" when req.Method == "POST" =>
                    (200, """{"versionCode":17300}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-err/tracks/alpha" when req.Method == "PUT" =>
                    (400, """{"error":{"code":400,"message":"Only releases with status draft may be created on draft app."}}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-err" when req.Method == "DELETE" =>
                    (204, ""),
                _ => (404, "{}"),
            };
        });

        var (exit, _, stderr) = RunPwsh($"""
            $sa = (Get-Content -LiteralPath {PsQuote(saPath)} -Raw) | ConvertFrom-Json
            Invoke-PlayUploadPipeline `
                -ServiceAccount $sa `
                -PackageName 'com.jeiel85.aiusage' `
                -AabPath {PsQuote(aabPath)} `
                -ExpectedVersionCode 17300 `
                -ReleaseName '1.73.0' `
                -Track 'alpha' `
                -Status 'completed' `
                -TokenUri {PsQuote(server.Url("/oauth2/token"))} `
                -ApiBaseUri {PsQuote(server.BaseUri)} `
                -AllowNonGoogleEndpointsForTest
            """);

        Assert.NotEqual(0, exit);
        Assert.Contains("-Status draft", stderr);
        Assert.Contains(server.Requests, r => r.Method == "DELETE" && r.Path == "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-err");
        Assert.DoesNotContain(server.Requests, r => r.Path.Contains(":commit"));
    }

    [Fact]
    public async Task UploadPipeline_AbortsAndDeletesEditOnVersionCodeMismatch()
    {
        using var rsa = RSA.Create(2048);
        var saPath = Path.Combine(_dir, "sa.json");
        File.WriteAllText(saPath, CreateServiceAccountJson(rsa), new UTF8Encoding(false));
        var aabPath = WriteMinimalAab("app-play-release.aab");

        await using var server = new MockPlayServer(req =>
        {
            return req.Path switch
            {
                "/oauth2/token" when req.Method == "POST" =>
                    (200, """{"access_token":"tok-1","token_type":"Bearer"}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits" when req.Method == "POST" =>
                    (200, """{"id":"edit-mismatch"}"""),
                "/upload/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-mismatch/bundles?uploadType=media" when req.Method == "POST" =>
                    (200, """{"versionCode":17299}"""),
                "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-mismatch" when req.Method == "DELETE" =>
                    (204, ""),
                _ => (404, "{}"),
            };
        });

        var (exit, _, stderr) = RunPwsh($"""
            $sa = (Get-Content -LiteralPath {PsQuote(saPath)} -Raw) | ConvertFrom-Json
            Invoke-PlayUploadPipeline `
                -ServiceAccount $sa `
                -PackageName 'com.jeiel85.aiusage' `
                -AabPath {PsQuote(aabPath)} `
                -ExpectedVersionCode 17300 `
                -ReleaseName '1.73.0' `
                -Track 'alpha' `
                -Status 'completed' `
                -TokenUri {PsQuote(server.Url("/oauth2/token"))} `
                -ApiBaseUri {PsQuote(server.BaseUri)} `
                -AllowNonGoogleEndpointsForTest
            """);

        Assert.NotEqual(0, exit);
        Assert.Contains("17299", stderr);
        Assert.Contains("17300", stderr);
        Assert.Contains(server.Requests, r => r.Method == "DELETE" && r.Path == "/androidpublisher/v3/applications/com.jeiel85.aiusage/edits/edit-mismatch");
        Assert.DoesNotContain(server.Requests, r => r.Method == "PUT" || r.Path.Contains(":commit"));
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }

    private sealed record RecordedRequest(string Method, string Path, string? Authorization, byte[] Body);

    private sealed class MockPlayServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly Func<RecordedRequest, (int StatusCode, string JsonBody)> _handler;

        public List<RecordedRequest> Requests { get; } = [];
        public string BaseUri => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
        public string Url(string path) => BaseUri + path;

        public MockPlayServer(Func<RecordedRequest, (int StatusCode, string JsonBody)> handler)
        {
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { return; }

                using (client)
                {
                    var stream = client.GetStream();
                    var rawReq = await ReadHttpRequestAsync(stream, _cts.Token);
                    if (rawReq is null) continue;
                    lock (Requests) Requests.Add(rawReq);

                    var (code, jsonBody) = _handler(rawReq);
                    var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
                    var reason = code switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", _ => "Error" };
                    var headers = $"HTTP/1.1 {code} {reason}\r\nContent-Type: application/json\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), _cts.Token);
                    if (bodyBytes.Length > 0)
                        await stream.WriteAsync(bodyBytes, _cts.Token);
                }
            }
        }

        private static async Task<RecordedRequest?> ReadHttpRequestAsync(NetworkStream stream, CancellationToken ct)
        {
            using var headerBuf = new MemoryStream();
            var single = new byte[1];
            int matchState = 0;
            while (matchState < 4)
            {
                var n = await stream.ReadAsync(single, ct);
                if (n == 0) return null;
                headerBuf.WriteByte(single[0]);
                matchState = single[0] switch
                {
                    (byte)'\r' when matchState is 0 or 2 => matchState + 1,
                    (byte)'\n' when matchState is 1 or 3 => matchState + 1,
                    (byte)'\r' => 1,
                    _ => 0,
                };
            }

            var headerText = Encoding.ASCII.GetString(headerBuf.GetBuffer(), 0, (int)headerBuf.Length);
            var lines = headerText.Split(["\r\n"], StringSplitOptions.None);
            var requestParts = lines[0].Split(' ');
            if (requestParts.Length < 2) return null;

            string? auth = null;
            int contentLength = 0;
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrEmpty(line)) break;
                var colon = line.IndexOf(':');
                if (colon <= 0) continue;
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                if (name.Equals("Authorization", StringComparison.OrdinalIgnoreCase)) auth = value;
                else if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out contentLength);
            }

            var body = new byte[contentLength];
            int offset = 0;
            while (offset < contentLength)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset, contentLength - offset), ct);
                if (read == 0) break;
                offset += read;
            }
            if (offset < contentLength) body = body[..offset];

            return new RecordedRequest(requestParts[0], requestParts[1], auth, body);
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
