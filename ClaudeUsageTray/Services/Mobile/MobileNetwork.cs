using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>이 PC 의 사설 LAN 주소와 서브넷 접두 길이.</summary>
public sealed record LanAddress(IPAddress Address, int PrefixLength);

/// <summary>
/// 휴대폰이 닿을 수 있는 이 PC 의 주소를 찾는다(Homebase <c>LanAddressResolver</c>·<c>TailscaleAddressResolver</c>).
/// 와일드카드(0.0.0.0)에 바인딩하지 않는 것이 핵심이다 — 공인 IP·VPN·가상 스위치 쪽으로 포트가 열리지 않게
/// 실제로 쓸 주소만 골라 그 주소에만 바인딩한다.
/// </summary>
public static class MobileNetwork
{
    /// <summary>RFC 1918: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16.</summary>
    public static bool IsPrivateV4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && (b[1] & 0xF0) == 16) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Tailscale 의 CGNAT 대역 100.64.0.0/10.</summary>
    public static bool IsTailnetV4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 100 && (b[1] & 0xC0) == 64;
    }

    /// <summary>
    /// 공인 라우팅 가능한 IP 주소인가(푸시 구독 SSRF 방어용, #340).
    /// 루프백·사설(RFC 1918)·CGNAT(100.64.0.0/10)·링크-로컬(169.254.0.0/16)·멀티캐스트·예약 대역과
    /// IPv6 비공인 대역(ULA·링크로컬·사이트로컬·문서용·루프백)을 모두 거부한다.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            if (b[0] == 0) return false;                              // 0.0.0.0/8 ("this" network)
            if (b[0] == 10) return false;                             // 10.0.0.0/8 (RFC 1918)
            if (b[0] == 100 && (b[1] & 0xC0) == 64) return false;     // 100.64.0.0/10 (CGNAT)
            if (b[0] == 127) return false;                            // 127.0.0.0/8 (loopback)
            if (b[0] == 169 && b[1] == 254) return false;             // 169.254.0.0/16 (link-local / cloud metadata)
            if (b[0] == 172 && (b[1] & 0xF0) == 16) return false;     // 172.16.0.0/12 (RFC 1918)
            if (b[0] == 192 && b[1] == 0 && b[2] == 0) return false;  // 192.0.0.0/24 (IETF protocol)
            if (b[0] == 192 && b[1] == 0 && b[2] == 2) return false;  // 192.0.2.0/24 (TEST-NET-1)
            if (b[0] == 192 && b[1] == 168) return false;             // 192.168.0.0/16 (RFC 1918)
            if (b[0] == 198 && (b[1] & 0xFE) == 18) return false;     // 198.18.0.0/15 (benchmarking)
            if (b[0] == 198 && b[1] == 51 && b[2] == 100) return false; // 198.51.100.0/24 (TEST-NET-2)
            if (b[0] == 203 && b[1] == 0 && b[2] == 113) return false;  // 203.0.113.0/24 (TEST-NET-3)
            if (b[0] >= 224) return false;                            // 224.0.0.0/4 multicast & 240.0.0.0/4 reserved
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return false;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || address.IsIPv6UniqueLocal)
                return false;
            var b = address.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                  // fc00::/7 (ULA)
            if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return false;  // fe80::/10 (link-local)
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32
            return true;
        }

        return false;
    }

    /// <summary>
    /// Tailscale 가상 어댑터 여부(#340). 통신사 CGNAT(모바일 테더링·Starlink·일부 ISP)도 100.64.0.0/10 을 쓰므로
    /// IP 대역만 보고 평문 HTTP 를 바인딩하면 비-Tailscale 공용망에 평문 서버가 열린다.
    /// </summary>
    internal static bool IsTailscaleInterface(string? name, string? description) =>
        (name is not null && name.Contains("tailscale", StringComparison.OrdinalIgnoreCase)) ||
        (description is not null && description.Contains("tailscale", StringComparison.OrdinalIgnoreCase));

    /// <summary><paramref name="peer"/> 가 <paramref name="local"/> 서브넷 중 하나 안에 있는가.</summary>
    public static bool InSameSubnet(IPAddress peer, IEnumerable<LanAddress> local)
    {
        if (peer.AddressFamily != AddressFamily.InterNetwork) return false;
        var p = ToUInt(peer);
        foreach (var l in local)
        {
            if (l.PrefixLength is < 1 or > 32) continue;
            var mask = l.PrefixLength == 32 ? uint.MaxValue : ~(uint.MaxValue >> l.PrefixLength);
            if ((p & mask) == (ToUInt(l.Address) & mask)) return true;
        }
        return false;
    }

    /// <summary>
    /// Input : 없음
    /// Output: 기본 게이트웨이가 있는 어댑터의 사설 IPv4 주소 목록
    /// 핵심 로직: 게이트웨이 조건으로 Hyper-V·WSL·VMware 호스트 전용 스위치(역시 사설 대역이지만 게이트웨이가 없음)를
    ///           QR 과 바인딩 목록에서 뺀다. 휴대폰이 닿을 수 없는 주소를 QR 에 넣으면 연결 시도마다 시간만 버린다.
    /// </summary>
    public static IReadOnlyList<LanAddress> FindLanAddresses()
    {
        try
        {
            var result = new List<LanAddress>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var props = nic.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                if (!hasGateway) continue;
                foreach (var ua in props.UnicastAddresses)
                {
                    if (IsPrivateV4(ua.Address) && !IsTailnetV4(ua.Address) &&
                        result.All(r => !r.Address.Equals(ua.Address)))
                        result.Add(new LanAddress(ua.Address, ua.PrefixLength));
                }
            }
            return result;
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>켜진 Tailscale 인터페이스에 붙은 Tailscale IPv4 주소(#340).</summary>
    public static IReadOnlyList<IPAddress> FindTailnetAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && IsTailscaleInterface(n.Name, n.Description))
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(IsTailnetV4)
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }

    /// <summary>
    /// <c>tailscale status --json</c> 의 MagicDNS 이름(예: <c>home-pc.tail1234.ts.net</c>). CLI 가 없거나 MagicDNS 가
    /// 꺼져 있으면 null — 최선 노력일 뿐이고 IP 만으로도 연결된다. 이름을 먼저 두는 이유: Tailscale IP 는 기기를
    /// 지웠다 다시 등록하면 바뀔 수 있지만 MagicDNS 이름은 PC 이름을 따라간다.
    /// </summary>
    public static string? FindMagicDnsName()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale", "tailscale.exe");
        if (!File.Exists(exe)) return null;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo(exe, "status --json --self=true --peers=false")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000)) { proc.Kill(); return null; }
            return ParseSelfDnsName(output);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string? ParseSelfDnsName(string statusJson)
    {
        using var doc = JsonDocument.Parse(statusJson);
        if (!doc.RootElement.TryGetProperty("Self", out var self) ||
            !self.TryGetProperty("DNSName", out var dns) || dns.ValueKind != JsonValueKind.String) return null;
        var name = dns.GetString()?.TrimEnd('.');
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static uint ToUInt(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }
}

/// <summary>
/// LAN 경로의 TLS 신원(Homebase DECISIONS M-15): 자체 서명 ECDSA P-256 인증서 하나를 현재 사용자 개인 저장소에 둔다.
/// QR 에 DER 의 SHA-256 을 싣고 앱은 그 인증서만 받는다 — CA·호스트 이름·만료 검사 대신 핀이다.
/// 사설 IP 에는 공인 인증서를 발급받을 수 없고, 평문이면 같은 Wi-Fi 의 누구나 토큰을 엿볼 수 있어서 이 방식을 쓴다.
/// 대가: 인증서를 지우면(저장소 정리 등) 휴대폰을 다시 페어링해야 한다.
/// </summary>
public static class MobileTlsCertificate
{
    public const string SubjectName = "CN=Claude Usage Tray Mobile LAN";

    /// <summary>개인키가 있는 인증서를 읽고, 없으면 만들어 저장한다.</summary>
    public static X509Certificate2 LoadOrCreate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var existing = store.Certificates
            .Find(X509FindType.FindBySubjectDistinguishedName, SubjectName, validOnly: false)
            .OfType<X509Certificate2>()
            .Where(c => c.HasPrivateKey)
            .OrderByDescending(c => c.NotAfter)
            .FirstOrDefault();
        if (existing is not null) return existing;

        var created = Create(persistKey: true);
        store.Add(created);
        return created;
    }

    /// <summary>
    /// 새 자체 서명 서버 인증서. SChannel 은 임시(ephemeral) 키를 쓰지 못하므로 키는 항상 키 컨테이너에 있어야 한다.
    /// 30년 유효 — 만료는 핀 방식에서 검사하지 않지만, 만료된 인증서를 거부하는 TLS 스택을 피하려고 길게 둔다.
    /// </summary>
    public static X509Certificate2 Create(bool persistKey)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest(SubjectName, key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // serverAuth
        var now = DateTimeOffset.UtcNow;
        using var ephemeral = req.CreateSelfSigned(now.AddDays(-1), now.AddYears(30));
        var cert = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pkcs12), null,
            persistKey ? X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.UserKeySet);
        if (OperatingSystem.IsWindows()) cert.FriendlyName = "Claude Usage Tray Mobile LAN";
        return cert;
    }

    /// <summary>핀: DER 인코딩 인증서의 SHA-256 소문자 hex.</summary>
    public static string Sha256Hex(X509Certificate2 cert) =>
        Convert.ToHexString(SHA256.HashData(cert.RawData)).ToLowerInvariant();
}
