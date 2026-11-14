package io.github.jeiel85.claudeusage.net

import okhttp3.OkHttpClient
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

/** The PC's LAN certificate is not the one in its pairing QR — refuse, then try another path. */
class PinMismatchException : CertificateException("PC certificate does not match the one from pairing")

/**
 * TLS for the LAN path (DECISIONS M-15): the PC's certificate is self-signed, so instead of a CA
 * the app trusts exactly the certificate whose SHA-256 came in the pairing QR. Anything else —
 * another device on a same-numbered home network, a man in the middle — fails the handshake.
 */
object PinnedTls {

    fun sha256Hex(der: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(der).joinToString("") { "%02x".format(it) }

    fun trustManager(pin: String): X509TrustManager = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<out X509Certificate>?, authType: String?) =
            throw CertificateException("client certificates are not used")

        override fun checkServerTrusted(chain: Array<out X509Certificate>?, authType: String?) {
            val leaf = chain?.firstOrNull() ?: throw CertificateException("no server certificate")
            if (!sha256Hex(leaf.encoded).equals(pin, ignoreCase = true)) throw PinMismatchException()
        }

        override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
    }

    /**
     * [base] with the pinned trust manager. The certificate names no host, so the hostname check
     * only confirms we're talking to the address we dialled; the pin is what authenticates the PC.
     */
    fun client(base: OkHttpClient, pin: String, expectedHost: String): OkHttpClient {
        val tm = trustManager(pin)
        val ctx = SSLContext.getInstance("TLS").apply { init(null, arrayOf(tm), null) }
        return base.newBuilder()
            .sslSocketFactory(ctx.socketFactory, tm)
            .hostnameVerifier { hostname, _ -> hostname.equals(expectedHost, ignoreCase = true) }
            .build()
    }
}
