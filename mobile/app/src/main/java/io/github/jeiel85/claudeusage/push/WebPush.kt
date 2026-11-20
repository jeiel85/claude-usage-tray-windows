package io.github.jeiel85.claudeusage.push

import java.math.BigInteger
import java.security.AlgorithmParameters
import java.security.GeneralSecurityException
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.interfaces.ECPrivateKey
import java.security.interfaces.ECPublicKey
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec
import java.util.Base64
import javax.crypto.Cipher
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * RFC 8291 Web Push decryption (`aes128gcm`, RFC 8188), the phone side of the PC's
 * `WebPushEncryption`. Platform crypto only — ECDH P-256, HMAC-SHA-256, AES-128-GCM — so the APK
 * gains no dependency (Homebase DECISIONS M-3, M-19; #222). The push server relays only this ciphertext.
 */
object WebPush {
    const val MAX_BODY_BYTES = 4096
    private const val HEADER_BYTES = 16 + 4 + 1 + 65

    private val KEY_INFO = "WebPush: info\u0000".toByteArray(Charsets.US_ASCII)
    private val CEK_INFO = "Content-Encoding: aes128gcm\u0000".toByteArray(Charsets.US_ASCII)
    private val NONCE_INFO = "Content-Encoding: nonce\u0000".toByteArray(Charsets.US_ASCII)

    /**
     * Decrypts one push body with this phone's key pair and auth secret.
     * Throws [GeneralSecurityException] for anything that isn't a valid message for us — a wrong
     * key or auth secret fails the GCM tag, so forged broadcasts are rejected here.
     */
    fun decrypt(body: ByteArray, uaPrivate: ECPrivateKey, uaPublic: ByteArray, authSecret: ByteArray): ByteArray {
        if (body.size < HEADER_BYTES + 17 || body.size > MAX_BODY_BYTES) throw GeneralSecurityException("bad length ${body.size}")
        if (body[20].toInt() and 0xff != 65) throw GeneralSecurityException("unexpected key id length")
        val salt = body.copyOfRange(0, 16)
        val asPublic = body.copyOfRange(21, HEADER_BYTES)
        val sealed = body.copyOfRange(HEADER_BYTES, body.size)

        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(uaPrivate)
        agreement.doPhase(publicKey(asPublic), true)
        val ecdhSecret = agreement.generateSecret()

        val prkKey = hmac(authSecret, ecdhSecret)
        val ikm = expand(prkKey, KEY_INFO + uaPublic + asPublic, 32)
        val prk = hmac(salt, ikm)
        val cek = expand(prk, CEK_INFO, 16)
        val nonce = expand(prk, NONCE_INFO, 12)

        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(cek, "AES"), GCMParameterSpec(128, nonce))
        val record = cipher.doFinal(sealed)

        // Single record: content, then the 0x02 delimiter, then optional zero padding (RFC 8188 §2).
        var end = record.size - 1
        while (end >= 0 && record[end].toInt() == 0) end--
        if (end < 0 || record[end].toInt() != 2) throw GeneralSecurityException("bad padding delimiter")
        return record.copyOf(end)
    }

    fun generateKeyPair(): KeyPair =
        KeyPairGenerator.getInstance("EC").apply { initialize(ECGenParameterSpec("secp256r1")) }.generateKeyPair()

    /** 65-byte uncompressed point (0x04 || X || Y), the `p256dh` the PC encrypts to. */
    fun uncompressed(key: ECPublicKey): ByteArray {
        fun fixed(n: BigInteger): ByteArray {
            val raw = n.toByteArray().dropWhile { it.toInt() == 0 }.toByteArray()
            require(raw.size <= 32) { "coordinate too large" }
            return ByteArray(32 - raw.size) + raw
        }
        return byteArrayOf(4) + fixed(key.w.affineX) + fixed(key.w.affineY)
    }

    /**
     * Imports a 65-byte uncompressed P-256 point and checks it lies on the curve, so a crafted
     * sender key can't be used for an invalid-curve attack on this phone's long-lived key.
     */
    fun publicKey(uncompressed: ByteArray): ECPublicKey {
        if (uncompressed.size != 65 || uncompressed[0].toInt() != 4) throw GeneralSecurityException("not an uncompressed P-256 point")
        val x = BigInteger(1, uncompressed.copyOfRange(1, 33))
        val y = BigInteger(1, uncompressed.copyOfRange(33, 65))
        if (!onCurve(x, y)) throw GeneralSecurityException("point is not on P-256")
        return KeyFactory.getInstance("EC").generatePublic(ECPublicKeySpec(ECPoint(x, y), p256)) as ECPublicKey
    }

    fun b64url(data: ByteArray): String = Base64.getUrlEncoder().withoutPadding().encodeToString(data)

    fun unb64url(text: String): ByteArray = Base64.getUrlDecoder().decode(text)

    private val p256: ECParameterSpec by lazy {
        AlgorithmParameters.getInstance("EC").apply { init(ECGenParameterSpec("secp256r1")) }
            .getParameterSpec(ECParameterSpec::class.java)
    }

    // y² = x³ − 3x + b (mod p), the P-256 curve equation.
    private val P = BigInteger("ffffffff00000001000000000000000000000000ffffffffffffffffffffffff", 16)
    private val B = BigInteger("5ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b", 16)

    private fun onCurve(x: BigInteger, y: BigInteger): Boolean {
        if (x.signum() < 0 || x >= P || y.signum() < 0 || y >= P) return false
        val lhs = y.modPow(BigInteger.valueOf(2), P)
        val rhs = x.modPow(BigInteger.valueOf(3), P).subtract(x.multiply(BigInteger.valueOf(3))).add(B).mod(P)
        return lhs == rhs
    }

    private fun hmac(key: ByteArray, data: ByteArray): ByteArray =
        Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(key, "HmacSHA256")) }.doFinal(data)

    /** HKDF-Expand for L ≤ 32: one block, T(1) = HMAC(PRK, info || 0x01). */
    private fun expand(prk: ByteArray, info: ByteArray, length: Int): ByteArray = hmac(prk, info + byteArrayOf(1)).copyOf(length)
}
