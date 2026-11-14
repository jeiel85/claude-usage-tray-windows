package io.github.jeiel85.claudeusage.net

import android.content.Context
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import java.net.Inet4Address

/**
 * Reads the phone's current networks for [RouteSelector]: the IPv4 subnets of non-VPN networks
 * (Wi-Fi, Ethernet) and whether a VPN with a Tailscale (100.64.0.0/10) address is up.
 * Cheap, synchronous, no polling — called once per connection attempt.
 */
class NetworkProbe(context: Context) {

    private val cm = context.applicationContext.getSystemService(ConnectivityManager::class.java)

    fun current(): LocalNetworks? = try {
        val subnets = mutableListOf<Subnet>()
        var tailnet = false
        @Suppress("DEPRECATION") // allNetworks is the only synchronous snapshot of every network.
        for (network in cm.allNetworks) {
            val caps = cm.getNetworkCapabilities(network) ?: continue
            val links = cm.getLinkProperties(network)?.linkAddresses ?: continue
            val isVpn = caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
            for (la in links) {
                val a = la.address as? Inet4Address ?: continue
                val b = a.address
                val ip = ((b[0].toInt() and 0xFF) shl 24) or ((b[1].toInt() and 0xFF) shl 16) or
                    ((b[2].toInt() and 0xFF) shl 8) or (b[3].toInt() and 0xFF)
                if (isVpn) {
                    if (RouteSelector.isTailnet(ip)) tailnet = true
                } else {
                    subnets += Subnet(ip, la.prefixLength)
                }
            }
        }
        LocalNetworks(subnets, tailnet)
    } catch (_: SecurityException) {
        null
    }
}
