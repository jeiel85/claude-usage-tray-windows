using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 터널 설정. <c>Token</c> 이 있으면 이름 있는 터널(고정 주소 <c>Hostname</c>), 없으면 Quick Tunnel.
/// <c>CloudflaredPath</c> 가 비어 있으면 설치 폴더와 PATH 에서 찾는다.
/// </summary>
public sealed record CloudflareTunnelOptions(int Port, string? Token = null, string? Hostname = null, string? CloudflaredPath = null);

/// <summary>설정 창에 보이는 터널 상태. <c>State</c>: starting · running · not_installed · error.</summary>
public sealed record CloudflareStatus(string State, string Mode, string? Hostname, string? Detail)
{
    public const string Starting = "starting";
    public const string Running = "running";
    public const string NotInstalled = "not_installed";
    public const string Error = "error";

    public const string Quick = "quick";
    public const string Named = "named";
}

/// <summary>
/// Cloudflare Tunnel 커넥터(<c>cloudflared</c>)를 이 앱의 하위 프로세스로 돌린다(#225, Homebase <c>CloudflareTunnel</c> 이식, M-17).
/// 커넥터가 바깥(Cloudflare)으로 연결해 127.0.0.1 의 휴대폰 API 로 넘겨 주므로, 인바운드 포트·방화벽 규칙이 필요 없다 —
/// Tailscale 없이 밖에서 휴대폰·다른 PC 가 닿는 길이다.
/// <list type="bullet">
/// <item>토큰 있음 → 그 이름 있는 터널(설정한 고정 호스트명). 토큰은 명령줄이 아니라 <c>TUNNEL_TOKEN</c> 환경 변수로 넘긴다
/// (명령줄은 다른 프로세스가 읽을 수 있다).</item>
/// <item>토큰 없음 → 계정 없는 Quick Tunnel. 주소(<c>*.trycloudflare.com</c>)는 커넥터 로그에서 읽고, 시작할 때마다 바뀐다.</item>
/// </list>
/// 프로세스는 kill-on-close Job Object 에 넣어 앱이 비정상 종료해도 남지 않는다. 실패하면 백오프(5초→15초→1분→5분)로
/// 다시 띄우고, 설치되어 있지 않으면 1분마다 다시 찾는다. 이 클래스의 실패는 트레이 본기능을 멈추지 않는다.
/// </summary>
public sealed partial class CloudflareTunnel : IDisposable
{
    private static readonly TimeSpan[] Backoff =
        [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];

    /// <summary>이만큼 버틴 커넥터는 정상으로 보고 백오프를 처음으로 돌린다.</summary>
    private static readonly TimeSpan HealthyRun = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan NotInstalledRecheck = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Quick Tunnel 주소. <c>api.trycloudflare.com</c> 은 커넥터가 Quick Tunnel 을 요청하는 API 주소라 오류 줄
    /// ("failed to request quick Tunnel: Post https://api.trycloudflare.com/…")에도 나온다 — 그것을 터널 주소로 착각하지 않게 뺀다.
    /// </summary>
    [GeneratedRegex(@"https://((?!api\.)[a-z0-9-]+\.trycloudflare\.com)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QuickTunnelUrl();

    [GeneratedRegex(@"Registered tunnel connection", RegexOptions.IgnoreCase)]
    private static partial Regex Registered();

    private readonly Func<string?, string?> _locate;
    private readonly object _gate = new();
    private CloudflareTunnelOptions? _options;
    private CancellationTokenSource? _cts;
    private CloudflareStatus? _status;
    private string? _lastError;

    /// <summary>상태가 바뀌면 알린다(스레드풀에서 올 수 있다). 서버는 이때 경로 목록을 다시 만든다.</summary>
    public event Action? Changed;

    /// <param name="locate">설정 경로 → cloudflared 실행 파일 경로(없으면 null). 테스트가 가짜 커넥터를 넣을 때 쓴다.</param>
    public CloudflareTunnel(Func<string?, string?>? locate = null) => _locate = locate ?? LocateCloudflared;

    /// <summary>
    /// Input : 터널 설정
    /// Output: 없음(백그라운드 루프 시작)
    /// 핵심 로직: 같은 설정으로 이미 돌고 있으면 아무것도 하지 않는다 — 설정 저장마다 터널을 다시 열면 Quick Tunnel 주소가
    ///           바뀌어, 밖에 있는 휴대폰이 경로 갱신 푸시를 받을 때까지 끊긴다.
    /// </summary>
    public void Start(CloudflareTunnelOptions options)
    {
        lock (_gate)
        {
            if (_cts is not null && _options == options) return;
        }
        Stop();
        lock (_gate)
        {
            _options = options;
            _cts = new CancellationTokenSource();
            _status = new CloudflareStatus(CloudflareStatus.Starting, ModeOf(options), NamedHostname(options), null);
            var token = _cts.Token;
            _ = Task.Run(() => RunAsync(options, token));
        }
        Changed?.Invoke();
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _cts;
            _cts = null;
            _status = null;
        }
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
        Changed?.Invoke();
    }

    /// <summary>터널 상태, 꺼져 있으면 null.</summary>
    public CloudflareStatus? Status()
    {
        lock (_gate) return _status;
    }

    /// <summary>터널이 열려 있을 때 휴대폰·다른 PC 가 쓸 경로(443, 공인 인증서 TLS).</summary>
    public MobileEndpoint? Endpoint()
    {
        lock (_gate)
        {
            return _status is { State: CloudflareStatus.Running, Hostname: { Length: > 0 } host }
                ? new MobileEndpoint(MobileTransport.Cloudflare, host, 443, true)
                : null;
        }
    }

    private static bool IsNamed(CloudflareTunnelOptions o) => !string.IsNullOrWhiteSpace(o.Token);

    private static string ModeOf(CloudflareTunnelOptions o) => IsNamed(o) ? CloudflareStatus.Named : CloudflareStatus.Quick;

    private static string? NamedHostname(CloudflareTunnelOptions o) => IsNamed(o) ? CleanHostname(o.Hostname) : null;

    private async Task RunAsync(CloudflareTunnelOptions options, CancellationToken ct)
    {
        if (IsNamed(options) && NamedHostname(options) is null)
        {
            // 토큰만으로는 그 터널에 어떤 공인 호스트명이 연결됐는지 알 수 없다(대시보드 설정) — 휴대폰에 줄 주소가 없다.
            SetStatus(ct, CloudflareStatus.Error, null, "hostname_required");
            return;
        }

        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            var exe = _locate(options.CloudflaredPath);
            if (exe is null)
            {
                SetStatus(ct, CloudflareStatus.NotInstalled, null, null);
                await DelaySafe(NotInstalledRecheck, ct);
                continue;
            }

            var started = DateTimeOffset.UtcNow;
            try
            {
                SetStatus(ct, CloudflareStatus.Starting, NamedHostname(options), null);
                var exitCode = await RunOnceAsync(exe, options, ct);
                if (ct.IsCancellationRequested) break;
                SetStatus(ct, CloudflareStatus.Error, null, $"cloudflared exited ({exitCode}). {LastError}".Trim());
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                SetStatus(ct, CloudflareStatus.Error, null, ex.Message);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            failures = DateTimeOffset.UtcNow - started >= HealthyRun ? 0 : failures + 1;
            await DelaySafe(Backoff[Math.Min(Math.Max(failures - 1, 0), Backoff.Length - 1)], ct);
        }
    }

    private string? LastError
    {
        get
        {
            lock (_gate) return _lastError;
        }
        set
        {
            lock (_gate) _lastError = value;
        }
    }

    private async Task<int> RunOnceAsync(string exe, CloudflareTunnelOptions options, CancellationToken ct)
    {
        LastError = null;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("tunnel");
        psi.ArgumentList.Add("--no-autoupdate");
        if (IsNamed(options))
        {
            psi.ArgumentList.Add("run");
            psi.Environment["TUNNEL_TOKEN"] = options.Token!.Trim();
        }
        else
        {
            psi.ArgumentList.Add("--url");
            psi.ArgumentList.Add($"http://127.0.0.1:{options.Port}");
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnLogLine(e.Data, options, ct);
        process.ErrorDataReceived += (_, e) => OnLogLine(e.Data, options, ct);
        if (!process.Start()) throw new InvalidOperationException("cloudflared did not start.");
        using var job = OperatingSystem.IsWindows() ? KillOnCloseJob.TryAssign(process) : null;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    /// <summary>
    /// Input : 커넥터 로그 한 줄
    /// Output: 없음(상태 갱신)
    /// 핵심 로직: Quick Tunnel 은 주소가 나온 줄, 이름 있는 터널은 "Registered tunnel connection" 줄을 열림으로 본다.
    ///           cloudflared 로그 형식은 버전마다 조금씩 다르므로 줄 전체가 아니라 주소·문구만 찾는다.
    /// </summary>
    private void OnLogLine(string? line, CloudflareTunnelOptions options, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(line) || ct.IsCancellationRequested) return;
        if (!IsNamed(options) && ParseQuickTunnelHost(line) is { } host)
        {
            SetStatus(ct, CloudflareStatus.Running, host, null);
        }
        else if (IsNamed(options) && Registered().IsMatch(line))
        {
            SetStatus(ct, CloudflareStatus.Running, NamedHostname(options), null);
        }
        else if (line.Contains(" ERR ", StringComparison.Ordinal))
        {
            LastError = line.Length > 300 ? line[..300] : line;
        }
    }

    /// <summary>지금 루프(<paramref name="owner"/>)의 상태만 반영한다 — 멈춘 직후 이전 루프가 늦게 덮어쓰지 않게.</summary>
    private void SetStatus(CancellationToken owner, string state, string? hostname, string? detail)
    {
        lock (_gate)
        {
            if (_cts is null || _cts.Token != owner || _options is null) return;
            var next = new CloudflareStatus(state, ModeOf(_options), hostname, detail);
            if (next == _status) return;
            _status = next;
        }
        Changed?.Invoke();
    }

    /// <summary>Quick Tunnel 로그 줄에 나온 <c>*.trycloudflare.com</c> 호스트(소문자), 없으면 null.</summary>
    public static string? ParseQuickTunnelHost(string line) =>
        QuickTunnelUrl().Match(line) is { Success: true } m ? m.Groups[1].Value.ToLowerInvariant() : null;

    /// <summary>스킴·경로·포트를 뗀 소문자 호스트명, 비었으면 null — 사용자가 URL 을 통째로 붙여 넣어도 되게.</summary>
    public static string? CleanHostname(string? raw)
    {
        var h = raw?.Trim();
        if (string.IsNullOrEmpty(h)) return null;
        var scheme = h.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) h = h[(scheme + 3)..];
        var cut = h.IndexOfAny(['/', ':', '?', '#']);
        if (cut >= 0) h = h[..cut];
        h = h.TrimEnd('.').ToLowerInvariant();
        return h.Length == 0 || !h.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.') ? null : h;
    }

    /// <summary>
    /// 설정한 경로 → winget·MSI 설치 폴더 → PATH 순. winget 으로 설치하면 PATH 는 새로 연 프로세스부터 바뀌므로,
    /// 이미 떠 있는 이 앱도 찾을 수 있게 설치 폴더를 먼저 본다.
    /// </summary>
    public static string? LocateCloudflared(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured) ? configured : null;
        var candidates = new List<string>();
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            var root = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(root)) candidates.Add(Path.Combine(root, "cloudflared", "cloudflared.exe"));
        }
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local)) candidates.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "cloudflared.exe"));
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "cloudflared.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException) { }
        }
        return null;
    }

    /// <summary>winget 패키지 ID(설정 창의 "설치" 버튼).</summary>
    public const string WingetPackage = "Cloudflare.cloudflared";

    /// <summary>
    /// Input : 없음
    /// Output: 성공하면 null, 실패하면 짧은 이유(winget 종료 코드·마지막 출력 줄)
    /// 핵심 로직: winget 으로 설치한다. 컴퓨터 전체 설치 관리자라 Windows 가 관리자 승인(UAC)을 물을 수 있다.
    ///           cloudflared 를 앱에 번들하지 않는 이유는 설치본이 수십 MB 커지고, 보안 업데이트를 이 앱 릴리스에 묶게 되기 때문이다.
    /// </summary>
    public static async Task<string?> InstallWithWingetAsync()
    {
        var psi = new ProcessStartInfo("winget.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "install", "--id", WingetPackage, "--exact", "--source", "winget", "--silent",
                     "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity" })
            psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return "winget did not start.";
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode == 0) return null;
            var text = (await stdout + "\n" + await stderr).Trim();
            var last = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            return $"winget 0x{p.ExitCode:X8}" + (last is null ? "" : $" — {last}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "winget_missing";
        }
    }

    private static async Task DelaySafe(TimeSpan delay, CancellationToken ct)
    {
        try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { }
    }

    public void Dispose() => Stop();

    /// <summary>KILL_ON_JOB_CLOSE Job Object: 앱이 죽으면(비정상 종료 포함) 커넥터도 함께 죽는다.</summary>
    private sealed class KillOnCloseJob : IDisposable
    {
        private readonly IntPtr _handle;

        private KillOnCloseJob(IntPtr handle) => _handle = handle;

        public static KillOnCloseJob? TryAssign(Process process)
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job == IntPtr.Zero) return null;
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
            var size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, (uint)size)
                || !AssignProcessToJobObject(job, process.Handle))
            {
                CloseHandle(job);
                return null;
            }
            return new KillOnCloseJob(job);
        }

        public void Dispose() => CloseHandle(_handle);

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
