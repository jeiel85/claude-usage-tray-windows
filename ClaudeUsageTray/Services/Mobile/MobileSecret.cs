using System.Security.Cryptography;
using System.Text;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// 저장할 비밀(동기화 토큰·터널 토큰)을 DPAPI(현재 사용자)로 감싼다. 설정·상태 파일이 OneDrive 등으로 동기화되거나
/// 백업되어도 다른 PC·다른 Windows 사용자는 풀 수 없다.
/// </summary>
public static class MobileSecret
{
    private const string PlainPrefix = "plain:";

    /// <summary>
    /// Input : 원문
    /// Output: DPAPI 암호문의 base64(Windows), 그 밖에서는 <c>plain:</c> 접두어 원문
    /// 핵심 로직: 앱은 Windows 에서만 돈다 — 리눅스 경로는 테스트 하네스(리눅스 CI·로컬)에서 같은 코드를 돌리기 위한 것이다.
    /// </summary>
    public static string Protect(string secret) =>
        OperatingSystem.IsWindows()
            ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser))
            : PlainPrefix + secret;

    /// <summary>풀 수 없으면 null — 다른 사용자·PC 에서 옮겨 온 파일이면 사용자가 다시 넣어야 한다.</summary>
    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal)) return stored[PlainPrefix.Length..];
            if (!OperatingSystem.IsWindows()) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
