using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ClaudeUsageTray.Services.Mobile;

/// <summary>
/// RFC 8291 "Message Encryption for Web Push" with the RFC 8188 <c>aes128gcm</c> content coding:
/// ECDH P-256 between a one-time application-server key and the phone's key, HKDF-SHA-256 mixed with
/// the phone's 16-byte auth secret, AES-128-GCM over a single record. Platform crypto only
/// (<see cref="System.Security.Cryptography"/>), no package dependency (Homebase DECISIONS M-19, 이 저장소 #222).
/// The push server (ntfy.sh or whichever distributor the phone uses) sees only ciphertext.
/// </summary>
public static class WebPushEncryption
{
    /// <summary>UnifiedPush and Web Push cap the encrypted body at 4096 bytes.</summary>
    public const int MaxBodyBytes = 4096;

    /// <summary>Header (16 salt + 4 rs + 1 idlen + 65 key) and the 16-byte GCM tag.</summary>
    public const int Overhead = 16 + 4 + 1 + 65 + 16;

    /// <summary>Largest plaintext that fits in <see cref="MaxBodyBytes"/> (one delimiter byte, no padding).</summary>
    public const int MaxPlaintextBytes = MaxBodyBytes - Overhead - 1;

    public const int RecordSize = 4096;

    private static readonly byte[] KeyInfoPrefix = Encoding.ASCII.GetBytes("WebPush: info\0");
    private static readonly byte[] CekInfo = Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0");
    private static readonly byte[] NonceInfo = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");

    /// <summary>Encrypts with a fresh application-server key pair and random salt.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> uaPublicKey, ReadOnlySpan<byte> authSecret)
    {
        using var asKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, uaPublicKey, authSecret, asKey, RandomNumberGenerator.GetBytes(16));
    }

    /// <summary>Deterministic form for tests (RFC 8291 Appendix A supplies the key and salt).</summary>
    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> uaPublicKey,
        ReadOnlySpan<byte> authSecret,
        ECDiffieHellman asKey,
        ReadOnlySpan<byte> salt)
    {
        if (plaintext.Length > MaxPlaintextBytes)
            throw new ArgumentException($"Plaintext of {plaintext.Length} bytes exceeds {MaxPlaintextBytes}.", nameof(plaintext));
        if (salt.Length != 16) throw new ArgumentException("Salt must be 16 bytes.", nameof(salt));
        ValidateSubscriptionKeys(uaPublicKey, authSecret);

        var asPublic = ExportUncompressed(asKey);
        using var ua = ImportPublic(uaPublicKey);
        var ecdhSecret = asKey.DeriveRawSecretAgreement(ua.PublicKey);
        var (cek, nonce) = DeriveKeys(ecdhSecret, authSecret, uaPublicKey, asPublic, salt);

        var record = new byte[plaintext.Length + 1];
        plaintext.CopyTo(record);
        record[^1] = 0x02; // last (only) record delimiter, no padding

        var body = new byte[16 + 4 + 1 + asPublic.Length + record.Length + 16];
        salt.CopyTo(body);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(16, 4), RecordSize);
        body[20] = (byte)asPublic.Length;
        asPublic.CopyTo(body.AsSpan(21));
        var ctStart = 21 + asPublic.Length;
        using (var gcm = new AesGcm(cek, 16))
        {
            gcm.Encrypt(nonce, record,
                body.AsSpan(ctStart, record.Length),
                body.AsSpan(ctStart + record.Length, 16));
        }
        return body;
    }

    /// <summary>
    /// The phone-side operation, used by tests to round-trip. Throws <see cref="CryptographicException"/>
    /// on a bad tag, a malformed header or a wrong padding delimiter (RFC 8291 §4).
    /// </summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> body, ECDiffieHellman uaKey, ReadOnlySpan<byte> authSecret)
    {
        if (body.Length < 21) throw new CryptographicException("Body too short.");
        var salt = body[..16];
        var idLen = body[20];
        if (idLen != 65 || body.Length < 21 + idLen + 16 + 1) throw new CryptographicException("Malformed header.");
        var asPublic = body.Slice(21, idLen);
        var sealedPart = body[(21 + idLen)..];

        var uaPublic = ExportUncompressed(uaKey);
        using var asKey = ImportPublic(asPublic);
        var ecdhSecret = uaKey.DeriveRawSecretAgreement(asKey.PublicKey);
        var (cek, nonce) = DeriveKeys(ecdhSecret, authSecret, uaPublic, asPublic, salt);

        var record = new byte[sealedPart.Length - 16];
        using (var gcm = new AesGcm(cek, 16))
        {
            gcm.Decrypt(nonce, sealedPart[..^16], sealedPart[^16..], record);
        }
        var end = record.Length - 1;
        while (end >= 0 && record[end] == 0) end--;
        if (end < 0 || record[end] != 0x02) throw new CryptographicException("Bad padding delimiter.");
        return record[..end];
    }

    /// <summary>
    /// Checks a subscription's keys: <paramref name="uaPublicKey"/> must be an uncompressed P-256
    /// point on the curve (65 bytes, 0x04 prefix) and <paramref name="authSecret"/> 16 bytes.
    /// </summary>
    public static void ValidateSubscriptionKeys(ReadOnlySpan<byte> uaPublicKey, ReadOnlySpan<byte> authSecret)
    {
        if (authSecret.Length != 16) throw new ArgumentException("Auth secret must be 16 bytes.", nameof(authSecret));
        using var _ = ImportPublic(uaPublicKey);
    }

    public static byte[] ExportUncompressed(ECDiffieHellman key)
    {
        var p = key.ExportParameters(includePrivateParameters: false);
        var result = new byte[65];
        result[0] = 0x04;
        p.Q.X!.CopyTo(result, 1);
        p.Q.Y!.CopyTo(result, 33);
        return result;
    }

    private static ECDiffieHellman ImportPublic(ReadOnlySpan<byte> uncompressed)
    {
        if (uncompressed.Length != 65 || uncompressed[0] != 0x04)
            throw new ArgumentException("Public key must be a 65-byte uncompressed P-256 point.");
        var key = ECDiffieHellman.Create();
        try
        {
            // ImportParameters validates that the point lies on the curve.
            key.ImportParameters(new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint { X = uncompressed.Slice(1, 32).ToArray(), Y = uncompressed.Slice(33, 32).ToArray() },
            });
            return key;
        }
        // Windows CNG reports an off-curve point as PlatformNotSupportedException ("curve or its
        // parameters are not valid") wrapping a CryptographicException.
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
        {
            key.Dispose();
            throw new ArgumentException("Public key is not a valid P-256 point.", ex);
        }
    }

    private static (byte[] Cek, byte[] Nonce) DeriveKeys(
        ReadOnlySpan<byte> ecdhSecret,
        ReadOnlySpan<byte> authSecret,
        ReadOnlySpan<byte> uaPublic,
        ReadOnlySpan<byte> asPublic,
        ReadOnlySpan<byte> salt)
    {
        // RFC 8291 §3.3–3.4. HKDF-Expand with L <= 32 is one HMAC block: T(1) = HMAC(PRK, info || 0x01).
        var prkKey = HMACSHA256.HashData(authSecret, ecdhSecret);
        var keyInfo = new byte[KeyInfoPrefix.Length + uaPublic.Length + asPublic.Length];
        KeyInfoPrefix.CopyTo(keyInfo, 0);
        uaPublic.CopyTo(keyInfo.AsSpan(KeyInfoPrefix.Length));
        asPublic.CopyTo(keyInfo.AsSpan(KeyInfoPrefix.Length + uaPublic.Length));
        var ikm = Expand(prkKey, keyInfo, 32);
        var prk = HMACSHA256.HashData(salt, ikm);
        return (Expand(prk, CekInfo, 16), Expand(prk, NonceInfo, 12));
    }

    private static byte[] Expand(byte[] prk, byte[] info, int length)
    {
        var input = new byte[info.Length + 1];
        info.CopyTo(input, 0);
        input[^1] = 0x01;
        return HMACSHA256.HashData(prk, input)[..length];
    }
}
