package io.github.jeiel85.claudeusage.net

import org.junit.Assert.assertEquals
import org.junit.Test

/** One QR, the right path automatically: same Wi-Fi → LAN, Tailscale VPN up → Tailscale. */
class RouteSelectorTest {

    private val lan = Endpoint(Endpoint.LAN, "192.168.45.101", 47830, true, "a".repeat(64))
    private val tsName = Endpoint(Endpoint.TAILSCALE, "home-pc.tail1234.ts.net", 47830, false)
    private val tsIp = Endpoint(Endpoint.TAILSCALE, "100.101.102.103", 47830, false)
    private val all = listOf(tsName, tsIp, lan) // deliberately not in preference order

    private fun subnet(ip: String, prefix: Int) = Subnet(RouteSelector.ipv4(ip)!!, prefix)

    @Test
    fun same_wifi_puts_lan_first() {
        val net = LocalNetworks(listOf(subnet("192.168.45.20", 24)), tailnetUp = true)
        assertEquals(listOf(lan, tsName, tsIp), RouteSelector.order(all, net, null))
    }

    @Test
    fun away_from_home_with_tailscale_prefers_tailscale() {
        val net = LocalNetworks(listOf(subnet("10.20.0.8", 16)), tailnetUp = true)
        assertEquals(listOf(tsName, tsIp, lan), RouteSelector.order(all, net, null))
    }

    @Test
    fun last_good_path_goes_first_within_its_rank() {
        val net = LocalNetworks(emptyList(), tailnetUp = true)
        assertEquals(listOf(tsIp, tsName, lan), RouteSelector.order(all, net, tsIp.key))
    }

    @Test
    fun unknown_network_tries_lan_then_tailscale() {
        assertEquals(listOf(lan, tsName, tsIp), RouteSelector.order(all, null, null))
    }

    private val cf = Endpoint(Endpoint.CLOUDFLARE, "bright-fox.trycloudflare.com", 443, true)

    @Test
    fun cloudflare_comes_after_direct_paths_but_before_a_tailscale_that_is_off() {
        val withCf = listOf(cf) + all
        // Away from home without Tailscale: Cloudflare is the path that works, so it goes first.
        val awayNoVpn = LocalNetworks(listOf(subnet("10.20.0.8", 16)), tailnetUp = false)
        assertEquals(listOf(cf, tsName, tsIp, lan), RouteSelector.order(withCf, awayNoVpn, null))
        // Same Wi-Fi: LAN first, Cloudflare still before the Tailscale VPN that is off.
        val home = LocalNetworks(listOf(subnet("192.168.45.20", 24)), tailnetUp = false)
        assertEquals(listOf(lan, cf, tsName, tsIp), RouteSelector.order(withCf, home, null))
        // Tailscale up: direct tailnet before the Cloudflare detour.
        val vpn = LocalNetworks(emptyList(), tailnetUp = true)
        assertEquals(listOf(tsName, tsIp, cf, lan), RouteSelector.order(withCf, vpn, null))
        // No network facts: LAN → Tailscale → Cloudflare.
        assertEquals(listOf(lan, tsName, tsIp, cf), RouteSelector.order(withCf, null, null))
    }

    @Test
    fun parses_only_dotted_ipv4_and_spots_the_tailnet_range() {
        assertEquals(null, RouteSelector.ipv4("home-pc.ts.net"))
        assertEquals(null, RouteSelector.ipv4("192.168.01.1"))
        assertEquals(true, RouteSelector.isTailnet(RouteSelector.ipv4("100.64.0.1")!!))
        assertEquals(false, RouteSelector.isTailnet(RouteSelector.ipv4("100.128.0.1")!!))
    }
}
