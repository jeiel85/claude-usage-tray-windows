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
            CLOUDFLARE -> "Cloudflare"
            LOOPBACK -> "loopback"
            else -> transport
        }

    /** Cloudflare's account-free tunnel: the address changes whenever the PC app restarts (#225). */
    val isQuickTunnel: Boolean get() = transport == CLOUDFLARE && host.endsWith(".trycloudflare.com", ignoreCase = true)

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

        /** Cloudflare Tunnel (#225): 443 with a public certificate, verified by the system CAs (no pin). */
        const val CLOUDFLARE = "cloudflare"

        /**
         * null for malformed entries, URL-injection hosts, unknown transports, or paths that violate
         * their transport's security invariant (#340):
         * - LAN: RFC 1918 private IPv4 + TLS + valid 64-hex certificate pin.
         * - Cloudflare: TLS + port 443 + public DNS hostname (no pin).
         * - Tailscale: 100.64.0.0/10 IPv4 or `*.ts.net` hostname.
         * - Loopback: localhost / 127.0.0.1 / ::1 / 10.0.2.2 (emulator).
         */
        fun parse(o: JSONObject): Endpoint? {
            val transport = o.optString("transport")
            val host = o.optString("host")
            val port = o.optInt("port", -1)
            val tls = o.optBoolean("tls")
            val pin = o.optString("certSha256").lowercase().ifBlank { null }
            if (transport.isBlank() || host.isBlank() || port !in 1..65535) return null
            if (host.length > 253 || host.any { it in FORBIDDEN_HOST_CHARS }) return null

            val valid = when (transport) {
                LAN -> tls && pin != null && pin.matches(PIN) && isPrivateIpv4(host)
                CLOUDFLARE -> tls && port == 443 && pin == null && isValidDnsHostname(host) && RouteSelector.ipv4(host) == null
                TAILSCALE -> RouteSelector.ipv4(host)?.let(RouteSelector::isTailnet) == true ||
                    (isValidDnsHostname(host) && host.endsWith(".ts.net", ignoreCase = true))
                LOOPBACK -> host.equals("localhost", ignoreCase = true) ||
                    host == "127.0.0.1" || host == "::1" || host == "10.0.2.2"
                else -> false
            }
            if (!valid) return null
            return Endpoint(transport, host, port, tls, pin)
        }

        fun parseList(arr: JSONArray?): List<Endpoint> = arr.objects().mapNotNull(::parse)

        private fun isPrivateIpv4(host: String): Boolean {
            val ip = RouteSelector.ipv4(host) ?: return false
            val b0 = ip ushr 24
            val b1 = (ip ushr 16) and 0xFF
            return b0 == 10 || (b0 == 172 && (b1 and 0xF0) == 16) || (b0 == 192 && b1 == 168)
        }

        private fun isValidDnsHostname(host: String): Boolean {
            if (host.isEmpty() || host.length > 253 || host.startsWith('.') || host.endsWith('.') || '.' !in host) return false
            return host.split('.').all { label ->
                label.isNotEmpty() && label.length <= 63 &&
                    label.first() != '-' && label.last() != '-' &&
                    label.all { it in 'a'..'z' || it in 'A'..'Z' || it in '0'..'9' || it == '-' }
            }
        }

        private val PIN = Regex("^[0-9a-f]{64}$")
        private val FORBIDDEN_HOST_CHARS = charArrayOf('@', '/', '\\', '?', '#', ' ', '\t', '\r', '\n')
    }
}
