package io.github.jeiel85.claudeusage.net

import org.json.JSONArray
import org.json.JSONObject

/**
 * One way to reach a PC (mirrors the PC's `MobileEndpoint`, docs/MOBILE_PROTOCOL.md). LAN endpoints
 * carry the SHA-256 of the PC's self-signed certificate; the app accepts exactly that certificate.
 * Ported from Homebase so both apps behave the same way.
 */
data class Endpoint(
    val transport: String,
    val host: String,
    val port: Int,
    val tls: Boolean,
    val certSha256: String? = null,
) {
    /** Stable identity for "last path that worked". */
    val key: String get() = "$transport|$host|$port"

    /** Short name shown as "connected via …" (proper nouns, same in every language). */
    val label: String
        get() = when (transport) {
            LAN -> "LAN"
            TAILSCALE -> "Tailscale"
            LOOPBACK -> "loopback"
            else -> transport
        }

    fun url(path: String): String {
        val scheme = if (tls) "https" else "http"
        val h = if (host.contains(':') && !host.startsWith("[")) "[$host]" else host
        val defaultPort = (tls && port == 443) || (!tls && port == 80)
        return if (defaultPort) "$scheme://$h$path" else "$scheme://$h:$port$path"
    }

    fun toJson(): JSONObject = JSONObject()
        .put("transport", transport)
        .put("host", host)
        .put("port", port)
        .put("tls", tls)
        .apply { certSha256?.let { put("certSha256", it) } }

    companion object {
        const val TAILSCALE = "tailscale"
        const val LAN = "lan"
        const val LOOPBACK = "loopback"

        /**
         * null for malformed entries, and for a LAN endpoint without a valid pin — a LAN path is
         * plain Wi-Fi underneath, so it is never trusted without the certificate pin from the QR.
         */
        fun parse(o: JSONObject): Endpoint? {
            val transport = o.optString("transport")
            val host = o.optString("host")
            val port = o.optInt("port", -1)
            val tls = o.optBoolean("tls")
            val pin = o.optString("certSha256").lowercase().ifBlank { null }
            if (transport.isBlank() || host.isBlank() || port !in 1..65535) return null
            if (transport == LAN && (!tls || pin == null || !pin.matches(PIN))) return null
            return Endpoint(transport, host, port, tls, pin)
        }

        fun parseList(arr: JSONArray?): List<Endpoint> = arr.objects().mapNotNull(::parse)

        private val PIN = Regex("^[0-9a-f]{64}$")
    }
}
