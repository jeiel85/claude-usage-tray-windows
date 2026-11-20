package io.github.jeiel85.claudeusage.net

/** An IPv4 subnet the phone is on (non-VPN networks only). */
data class Subnet(val address: Int, val prefixLength: Int) {
    fun contains(ip: Int): Boolean {
        if (prefixLength !in 1..32) return false
        val mask = if (prefixLength == 32) -1 else (-1 shl (32 - prefixLength))
        return (ip and mask) == (address and mask)
    }
}

/** What the phone's own network looks like right now (see [NetworkProbe]). */
data class LocalNetworks(val subnets: List<Subnet>, val tailnetUp: Boolean)

/**
 * Orders a PC's endpoints for this moment, so one QR works at home and away (Homebase DECISIONS M-18):
 *
 * 1. LAN address inside a subnet the phone is on — same Wi-Fi, fastest.
 * 2. Tailscale while the phone's Tailscale VPN is up.
 * 3. Cloudflare Tunnel (#225) — works from anywhere with internet, no Tailscale needed, but every byte
 *    goes through Cloudflare, so it comes after the direct paths.
 * 4. The rest (Tailscale with the VPN apparently down, LAN on another subnet, loopback) as a last try.
 *
 * Every endpoint stays in the list: detection is a hint, not a filter (a wrong guess only costs one
 * timeout). Within a rank the path that worked last goes first, then the PC's own order. With no
 * network facts the order is LAN → Tailscale → Cloudflare.
 */
object RouteSelector {

    fun order(endpoints: List<Endpoint>, net: LocalNetworks?, lastGoodKey: String?): List<Endpoint> =
        endpoints.withIndex()
            .sortedWith(
                compareBy<IndexedValue<Endpoint>> { rank(it.value, net) }
                    .thenBy { if (it.value.key == lastGoodKey) 0 else 1 }
                    .thenBy { it.index },
            )
            .map { it.value }

    private fun rank(e: Endpoint, net: LocalNetworks?): Int {
        if (net == null) {
            return when (e.transport) {
                Endpoint.LAN -> 0
                Endpoint.TAILSCALE -> 1
                Endpoint.CLOUDFLARE -> 2
                else -> 5
            }
        }
        return when (e.transport) {
            Endpoint.LAN -> if (ipv4(e.host)?.let { ip -> net.subnets.any { it.contains(ip) } } == true) 0 else 4
            Endpoint.TAILSCALE -> if (net.tailnetUp) 1 else 3
            Endpoint.CLOUDFLARE -> 2
            else -> 5
        }
    }

    /** Dotted IPv4 → int, or null for names / IPv6. */
    fun ipv4(text: String): Int? {
        val parts = text.split('.')
        if (parts.size != 4) return null
        var v = 0
        for (p in parts) {
            val n = p.toIntOrNull() ?: return null
            if (n !in 0..255 || p.isEmpty() || (p.length > 1 && p[0] == '0')) return null
            v = (v shl 8) or n
        }
        return v
    }

    /** Tailscale's CGNAT range 100.64.0.0/10. */
    fun isTailnet(ip: Int): Boolean = (ip ushr 24) == 100 && ((ip ushr 16) and 0xC0) == 64
}
