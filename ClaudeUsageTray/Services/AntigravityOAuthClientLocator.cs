using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClaudeUsageTray.Services;

/// <summary>OAuth client 자격 증명 한 쌍. 바이너리에서 뽑은 후보이거나 사용자가 적어 둔 값이다.</summary>
public sealed record AntigravityOAuthClient(string ClientId, string ClientSecret);

/// <summary>
/// 설치된 Antigravity 의 <c>language_server.exe</c> 에서 OAuth client_id/secret 을 찾아낸다.
///
/// 자격 증명 관리자에 남아 있는 access_token 이 아직 살아 있으면 이 경로는 쓰이지 않는다.
/// 토큰이 만료돼 refresh 가 필요할 때만 호출된다.
///
/// client_id 와 secret 은 바이너리에 평문으로 박혀 있지만 인접한 다른 문자열과 경계 없이 붙어 있어서,
/// 문자 종류만으로 앞뒤를 자르면 옆 문자열을 함께 먹는다. 그래서 Google 이 정한 형식
/// (<c>{숫자}-{소문자숫자}.apps.googleusercontent.com</c>, <c>GOCSPX-</c> + 28자) 을 그대로 경계로 쓴다.
/// </summary>
internal static class AntigravityOAuthClientLocator
{
    private const string IdSuffix = ".apps.googleusercontent.com";
    private const string SecretPrefix = "GOCSPX-";

    /// <summary>Google client secret 은 접두사 뒤 28자 고정이다. 경계가 없는 바이너리에서는 이 길이가 유일한 단서다.</summary>
    private const int SecretBodyLength = 28;

    private const int MinProjectDigits = 6;
    private const int MinProjectHash = 15;

    /// <summary>설치된 language_server.exe 경로. 없으면 null.</summary>
    internal static string? FindLanguageServerPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(localAppData)) return null;

        var path = Path.Combine(localAppData, "Programs", "antigravity", "resources", "bin", "language_server.exe");
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 바이너리를 훑어 client_id 와 secret 후보를 모아 가능한 조합을 만든다.
    /// 어느 조합이 실제로 유효한지는 여기서 알 수 없으므로 (짝이 기록돼 있지 않다) 호출부가 차례로 시도한다.
    /// 보통 각각 2개씩이라 조합은 네 개를 넘지 않는다.
    /// </summary>
    internal static IReadOnlyList<AntigravityOAuthClient> ScanCandidates(Stream stream)
    {
        var ids = new List<string>();
        var secrets = new List<string>();
        ScanRaw(stream, ids, secrets);

        var pairs = new List<AntigravityOAuthClient>(ids.Count * secrets.Count);
        foreach (var id in ids)
            foreach (var secret in secrets)
                pairs.Add(new AntigravityOAuthClient(id, secret));
        return pairs;
    }

    private static void ScanRaw(Stream stream, List<string> ids, List<string> secrets)
    {
        byte[] suffix = Encoding.ASCII.GetBytes(IdSuffix);
        byte[] prefix = Encoding.ASCII.GetBytes(SecretPrefix);

        // 청크 경계에 걸친 문자열을 놓치지 않도록 앞 chunk 의 꼬리를 물고 간다.
        const int ChunkSize = 4 * 1024 * 1024;
        int overlap = Math.Max(suffix.Length, prefix.Length + SecretBodyLength) + 128;

        var buffer = new byte[ChunkSize + overlap];
        int carry = 0;

        while (true)
        {
            int read = stream.Read(buffer, carry, ChunkSize);
            if (read <= 0) break;

            int length = carry + read;
            ScanBuffer(buffer, length, suffix, prefix, ids, secrets);

            carry = Math.Min(overlap, length);
            Buffer.BlockCopy(buffer, length - carry, buffer, 0, carry);
        }
    }

    private static void ScanBuffer(byte[] buffer, int length, byte[] suffix, byte[] prefix,
                                   List<string> ids, List<string> secrets)
    {
        for (int i = 0; i + suffix.Length <= length; i++)
        {
            if (!Matches(buffer, i, suffix)) continue;
            var id = ReadClientIdBackwards(buffer, i);
            if (id is not null && !ids.Contains(id)) ids.Add(id);
        }

        for (int i = 0; i + prefix.Length + SecretBodyLength <= length; i++)
        {
            if (!Matches(buffer, i, prefix)) continue;

            bool bodyIsSecretChars = true;
            for (int k = 0; k < SecretBodyLength; k++)
            {
                if (!IsSecretChar(buffer[i + prefix.Length + k])) { bodyIsSecretChars = false; break; }
            }
            if (!bodyIsSecretChars) continue;

            var secret = Encoding.ASCII.GetString(buffer, i, prefix.Length + SecretBodyLength);
            if (!secrets.Contains(secret)) secrets.Add(secret);
        }
    }

    /// <summary>
    /// <c>.apps.googleusercontent.com</c> 앞에서 거꾸로 읽어 client_id 를 복원한다.
    /// 형식(해시 - 하이픈 - 숫자)에서 벗어나면 앞 문자열을 먹은 것이므로 버린다.
    /// </summary>
    private static string? ReadClientIdBackwards(byte[] buffer, int suffixStart)
    {
        int hashStart = suffixStart;
        while (hashStart > 0 && IsLowerAlphanumeric(buffer[hashStart - 1])) hashStart--;
        if (suffixStart - hashStart < MinProjectHash) return null;

        int hyphen = hashStart - 1;
        if (hyphen <= 0 || buffer[hyphen] != (byte)'-') return null;

        int digitsStart = hyphen;
        while (digitsStart > 0 && IsDigit(buffer[digitsStart - 1])) digitsStart--;
        if (hyphen - digitsStart < MinProjectDigits) return null;

        return Encoding.ASCII.GetString(buffer, digitsStart, suffixStart - digitsStart) + IdSuffix;
    }

    private static bool Matches(byte[] buffer, int offset, byte[] pattern)
    {
        for (int k = 0; k < pattern.Length; k++)
        {
            if (buffer[offset + k] != pattern[k]) return false;
        }
        return true;
    }

    private static bool IsDigit(byte b) => b >= (byte)'0' && b <= (byte)'9';

    private static bool IsLowerAlphanumeric(byte b) =>
        (b >= (byte)'a' && b <= (byte)'z') || IsDigit(b);

    private static bool IsSecretChar(byte b) =>
        (b >= (byte)'A' && b <= (byte)'Z') || (b >= (byte)'a' && b <= (byte)'z') ||
        IsDigit(b) || b == (byte)'_' || b == (byte)'-';
}
