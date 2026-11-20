package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File
import java.math.BigInteger
import java.security.GeneralSecurityException
import java.security.KeyFactory
import java.security.interfaces.ECPrivateKey
import java.security.spec.ECPrivateKeySpec

/**
 * RFC 8291 Appendix A vectors, plus the PC-encrypted fixture `push-message.v1.aes128gcm.txt`, so the
 * PC's encryption and this app's decryption are checked against each other (#222).
 */
class WebPushTest {

    private val uaPublic = WebPush.unb64url("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4")
    private val uaPrivate = WebPush.unb64url("q1dXpw3UpT5VOmu_cf_v6ih07Aems3njxI-JWgLcM94")
    private val auth = WebPush.unb64url("BTBZMqHH6r4Tts7J_aSIgg")
    private val header = "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8"
    private val ciphertext = "8pfeW0KbunFT06SuDKoJH9Ql87S1QUrdirN6GcG7sFz1y1sqLgVi1VhjVkHsUoEsbI_0LpXMuGvnzQ"

    private fun privateKey(): ECPrivateKey {
        val params = WebPush.publicKey(uaPublic).params
        return KeyFactory.getInstance("EC").generatePrivate(ECPrivateKeySpec(BigInteger(1, uaPrivate), params)) as ECPrivateKey
    }

    private fun body() = WebPush.unb64url(header) + WebPush.unb64url(ciphertext)

    private fun fixture(name: String): String {
        val candidates = listOf(File("../../docs/mobile-protocol/$name"), File("docs/mobile-protocol/$name"))
        return candidates.first { it.exists() }.readText(Charsets.UTF_8)
    }

    @Test
    fun decrypts_the_rfc_8291_appendix_a_message() {
        assertEquals("When I grow up, I want to be a watermelon", WebPush.decrypt(body(), privateKey(), uaPublic, auth).toString(Charsets.UTF_8))
    }

    @Test
    fun decrypts_what_the_pc_encrypted_and_it_matches_the_message_fixture() {
        val encrypted = WebPush.unb64url(fixture("push-message.v1.aes128gcm.txt").trim())
        val plain = WebPush.decrypt(encrypted, privateKey(), uaPublic, auth).toString(Charsets.UTF_8)
        val parsed = PushMessage.parse(plain)
        assertEquals(PushMessage.parse(fixture("push-message.v1.json")), parsed)
        assertEquals("usage", parsed.category)
        assertEquals("HOME-PC", parsed.pcName)
        assertEquals(300, parsed.eventToleranceSec)
    }

    @Test(expected = GeneralSecurityException::class)
    fun a_flipped_bit_fails_the_tag() {
        val b = body()
        b[b.size - 1] = (b[b.size - 1].toInt() xor 1).toByte()
        WebPush.decrypt(b, privateKey(), uaPublic, auth)
    }

    @Test(expected = GeneralSecurityException::class)
    fun a_wrong_auth_secret_fails() {
        WebPush.decrypt(body(), privateKey(), uaPublic, ByteArray(16))
    }

    @Test(expected = GeneralSecurityException::class)
    fun a_sender_key_off_the_curve_is_refused_before_ecdh() {
        val b = body()
        b[85] = (b[85].toInt() xor 1).toByte()
        WebPush.decrypt(b, privateKey(), uaPublic, auth)
    }
}
