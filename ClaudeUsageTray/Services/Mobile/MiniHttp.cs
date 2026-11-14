using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>HTTP/1.1 요청 머리. 헤더 이름은 대소문자를 가리지 않는다.</summary>
public sealed record HttpRequestHead(string Method, string Path, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    public string? BearerToken()
    {
        var auth = Header("Authorization");
        const string prefix = "Bearer ";
        return auth is not null && auth.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? auth[prefix.Length..].Trim()
            : null;
    }

    public bool IsWebSocketUpgrade =>
        string.Equals(Header("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase) &&
        Header("Sec-WebSocket-Key") is { Length: > 0 };
}

/// <summary>
/// 휴대폰 API 에 필요한 가장 작은 HTTP/1.1: 연결당 요청 하나(<c>Connection: close</c>), 머리·본문 크기 제한,
/// RFC 6455 업그레이드 핸드셰이크. Homebase <c>MiniHttp</c> 를 옮겨 왔다.
///
/// 왜 HttpListener/Kestrel 이 아닌가(Homebase DECISIONS M-6): http.sys 는 특정 IP 프리픽스여도 포트를 모든
/// 인터페이스에 열고 관리자 URL 예약(netsh http add urlacl)이 필요하다. Kestrel 은 트레이 앱에 ASP.NET 런타임을
/// 싣는다. 명시한 주소에만 바인딩하는 소켓이면 라우트 다섯 개에 충분하다.
/// </summary>
public static class MiniHttp
{
    public const int MaxHeadBytes = 8 * 1024;
    public const int MaxBodyBytes = 4 * 1024;

    private const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    /// <summary>
    /// Input : 연결 스트림
    /// Output: 요청 머리와 같은 읽기에 딸려 온 본문 바이트. 형식이 틀리거나 8KB 를 넘으면 null
    /// 핵심 로직: 머리 크기를 고정 버퍼로 묶는다 — 인증 전 단계라 누구든 보낼 수 있으므로 메모리를 무한히 쓰지 않게.
    /// </summary>
    public static async Task<(HttpRequestHead Head, byte[] Leftover)?> ReadHeadAsync(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxHeadBytes];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled), ct);
            if (read == 0) return null;
            filled += read;
            var end = buffer.AsSpan(0, filled).IndexOf("\r\n\r\n"u8);
            if (end >= 0)
            {
                var head = Parse(Encoding.ASCII.GetString(buffer, 0, end));
                if (head is null) return null;
                return (head, buffer.AsSpan(end + 4, filled - end - 4).ToArray());
            }
        }
        return null;
    }

    public static HttpRequestHead? Parse(string head)
    {
        var lines = head.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length != 3 || !requestLine[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) return null;
        var path = requestLine[1];
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) return null;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        return new HttpRequestHead(requestLine[0].ToUpperInvariant(), path, headers);
    }

    /// <summary>Content-Length 만큼(4KB 이하) 본문을 읽는다. 머리와 함께 이미 받은 바이트를 먼저 쓴다.</summary>
    public static async Task<byte[]?> ReadBodyAsync(Stream stream, HttpRequestHead head, byte[] leftover, CancellationToken ct)
    {
        if (!int.TryParse(head.Header("Content-Length"), out var length) || length < 0 || length > MaxBodyBytes)
            return null;
        var body = new byte[length];
        var have = Math.Min(leftover.Length, length);
        Array.Copy(leftover, body, have);
        if (have < length) await stream.ReadExactlyAsync(body.AsMemory(have), ct);
        return body;
    }

    public static async Task WriteResponseAsync(Stream stream, int status, string? jsonBody, CancellationToken ct)
    {
        var body = jsonBody is null ? [] : Encoding.UTF8.GetBytes(jsonBody);
        var sb = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n");
        if (jsonBody is not null) sb.Append("Content-Type: application/json; charset=utf-8\r\n");
        sb.Append("\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
        if (body.Length > 0) await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task WriteUpgradeAsync(Stream stream, string secWebSocketKey, CancellationToken ct)
    {
        var response =
            "HTTP/1.1 101 Switching Protocols\r\n" +
            "Upgrade: websocket\r\n" +
            "Connection: Upgrade\r\n" +
            "Sec-WebSocket-Accept: " + WebSocketAccept(secWebSocketKey) + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(response), ct);
        await stream.FlushAsync(ct);
    }

    public static string WebSocketAccept(string key) =>
        Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketGuid)));

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        405 => "Method Not Allowed",
        410 => "Gone",
        429 => "Too Many Requests",
        503 => "Service Unavailable",
        _ => "Error",
    };
}
