using System.Security.Cryptography;
using System.Text;

namespace ClaudeUsageTray.Services;

/// <summary>
/// 자동 업데이트 서명(#290, <c>docs/DECISIONS.md</c> D-1). 릴리스의 SHA256.txt 는 같은 릴리스에 함께 올라가므로 출처를 증명하지
/// 못한다 — 릴리스를 바꿀 수 있는 쪽(탈취된 GitHub 계정·토큰·Action)이 exe 와 해시를 함께 바꾸면 그대로 설치됐다.
/// 그래서 GitHub 밖(배포하는 PC)에만 있는 개인키로 <c>버전 + exe SHA-256</c> 에 서명하고, 앱은 내장한 공개키로 확인한 exe 만 설치한다.
/// 서명은 <c>tools/sign-release.ps1</c> 이 만들어 릴리스 자산 <see cref="AssetName"/> 으로 올린다.
/// </summary>
public static class UpdateSignature
{
    /// <summary>릴리스 자산 이름. 내용은 서명(IEEE P1363, 64바이트)의 base64 한 줄.</summary>
    public const string AssetName = "ClaudeUsageTray.exe.sig";

    /// <summary>
    /// 업데이트 서명 공개키(ECDSA P-256, SubjectPublicKeyInfo). 개인키는 배포 PC 의 메인 체크아웃 <c>.keystore/update-signing.pem</c>
    /// (비밀번호 암호화, git 제외)와 Google Drive 백업에만 있다. 바꾸면 이 키를 가진 이전 버전은 새 키로 서명된 업데이트를 받지 못한다.
    /// </summary>
    public const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEHQSPlH11VxsPrMzYMwhl9dTzYAsw
        9l9QG54Upkm+XbTfO4gacZ1XPBaoewUx384cAbrvdXKUlO968tcDGPKJEg==
        -----END PUBLIC KEY-----
        """;

    /// <summary>
    /// 서명하는 내용. 버전을 함께 묶어, 예전에 서명된 exe 를 더 높은 버전 태그로 다시 올려 옛 버전으로 되돌리는 공격을 막는다.
    /// 앞머리는 이 서명이 다른 용도의 서명으로 쓰이지 않게 하는 구분자다. 서명 스크립트와 한 글자도 달라선 안 된다.
    /// </summary>
    public static byte[] Message(Version version, string sha256Hex) =>
        Encoding.UTF8.GetBytes($"claude-usage-tray-update/v1\n{version.ToString(3)}\n{sha256Hex.Trim().ToLowerInvariant()}\n");

    /// <summary>내장 공개키로 확인한다.</summary>
    public static bool Verify(Version version, string sha256Hex, string? signatureBase64) =>
        Verify(PublicKeyPem, version, sha256Hex, signatureBase64);

    /// <summary>
    /// Input : 공개키 PEM, 업데이트 버전, 받은 exe 의 SHA-256(hex), 서명 자산 내용
    /// Output: 서명이 맞는가. 형식이 틀린 서명·키도 false(예외를 내지 않는다)
    /// </summary>
    public static bool Verify(string publicKeyPem, Version version, string sha256Hex, string? signatureBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(sha256Hex)) return false;
        try
        {
            var signature = Convert.FromBase64String(signatureBase64.Trim());
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            if (key.KeySize != 256) return false;
            return key.VerifyData(Message(version, sha256Hex), signature, HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
