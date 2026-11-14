package io.github.jeiel85.claudeusage.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.time.OffsetDateTime

/**
 * Parses the same fixtures the PC's MobileProtocolGoldenTests serialize (docs/mobile-protocol), so
 * the PC and the app can't drift apart silently.
 */
class ProtocolTest {

    private fun fixture(name: String): String {
        // Gradle runs unit tests with the module directory (mobile/app) as the working directory.
        val candidates = listOf(File("../../docs/mobile-protocol/$name"), File("docs/mobile-protocol/$name"))
        return candidates.first { it.exists() }.readText(Charsets.UTF_8)
    }

    private fun ms(iso: String) = OffsetDateTime.parse(iso).toInstant().toEpochMilli()

    @Test
    fun snapshot_fixture_parses_every_provider() {
        val s = UsageSnapshot.parse(fixture("usage-snapshot.v1.json"))
        assertEquals("HOME-PC", s.pcName)
        assertEquals("0123456789abcdef0123456789abcdef", s.pcId)
        assertEquals(ms("2026-11-14T03:00:00+00:00"), s.tsMs)
        assertEquals(listOf("claude", "codex", "antigravity"), s.providers.map { it.provider })

        val claude = s.providers[0]
        assertTrue(claude.isAccountScoped)
        assertEquals("김민준 · Acme Corp", claude.accountLabel)
        val q = claude.quota!!
        assertTrue(q.hasData)
        assertEquals("max", q.planType)
        val short = q.gauges.first { it.kind == GaugeKind.SHORT }
        assertEquals(0.42, short.usage, 1e-9)
        assertEquals(300, short.windowMinutes) // Claude's 5-hour window, implied
        assertEquals(ms("2026-11-14T05:00:00+00:00"), short.resetAtMs)
        val long = q.gauges.first { it.kind == GaugeKind.LONG }
        assertEquals(0.18, long.usage, 1e-9)
        assertEquals(7 * 24 * 60, long.windowMinutes)
        val opus = q.gauges.first { it.kind == GaugeKind.NAMED }
        assertEquals("Opus", opus.name)
        assertEquals(0.7, opus.usage, 1e-9)
        val extra = q.gauges.first { it.kind == GaugeKind.EXTRA }
        assertEquals(0.05, extra.usage, 1e-9)
        assertEquals("$2.50 / $50.00", q.extraCreditsLabel)
        assertEquals(2_734_000L, claude.today!!.totalTokens)
        assertEquals("2026-11-14", claude.today!!.date)

        val codex = s.providers[1]
        assertEquals(listOf(GaugeKind.SHORT, GaugeKind.LONG), codex.quota!!.gauges.map { it.kind })
        assertEquals(10080, codex.quota!!.gauges[1].windowMinutes)
        assertNull(codex.today)

        val antigravity = s.providers[2]
        assertEquals(ProviderUsage.SCOPE_DEVICE, antigravity.accountScope)
        assertEquals("api_failed", antigravity.errorKind)
        assertNull(antigravity.quota)
    }

    @Test
    fun pairing_link_fixture_decodes_to_an_offer() {
        val link = fixture("pairing-link.v1.txt").trim()
        assertTrue(PairingLink.isLink(link))
        val offer = PairingOffer.parse(link)
        assertNotNull(offer)
        assertEquals("HOME-PC", offer!!.pcName)
        assertEquals(4, offer.endpoints.size)
        val cf = offer.endpoints.single { it.transport == Endpoint.CLOUDFLARE }
        assertTrue(cf.tls)
        assertNull(cf.certSha256) // public certificate: system CAs, no pin
        assertTrue(cf.isQuickTunnel)
        assertEquals("https://bright-fox-quiet-river.trycloudflare.com/api/v1/live", cf.url("/api/v1/live"))
        val lan = offer.endpoints.first { it.transport == Endpoint.LAN }
        assertTrue(lan.tls)
        assertEquals(64, lan.certSha256!!.length)
        assertEquals("https://192.168.0.10:47830/api/v1/live", lan.url("/api/v1/live"))
    }

    @Test
    fun raw_offer_json_parses_and_foreign_qr_is_rejected() {
        assertNotNull(PairingOffer.parse(fixture("pairing-offer.v1.json")))
        // A Homebase QR (hb instead of cu) is not this app's code.
        assertNull(PairingOffer.parse("""{"hb":1,"pcId":"x","pcName":"y","code":"z","endpoints":[]}"""))
        assertNull(PairingOffer.parse("hello"))
    }

    @Test
    fun endpoints_fixture_parses() {
        val list = parseEndpointsResponse(fixture("endpoints.v1.json"))
        assertEquals(listOf(Endpoint.LAN, Endpoint.TAILSCALE, Endpoint.TAILSCALE, Endpoint.CLOUDFLARE), list.map { it.transport })
    }

    @Test(expected = ProtocolException::class)
    fun other_schema_is_refused_rather_than_half_parsed() {
        UsageSnapshot.parse("""{"schema":2,"pcId":"x","providers":[]}""")
    }

    @Test
    fun lan_endpoint_without_pin_is_dropped() {
        val json = org.json.JSONObject("""{"transport":"lan","host":"192.168.0.2","port":47830,"tls":true}""")
        assertNull(Endpoint.parse(json))
    }

    @Test
    fun passed_reset_time_shows_an_empty_window() {
        val g = Gauge(GaugeKind.SHORT, "", 0.9, resetAtMs = 1_000L, windowMinutes = 300)
        assertEquals(0.0, g.effective(nowMs = 2_000L).usage, 0.0)
        assertEquals(0.9, g.effective(nowMs = 500L).usage, 0.0)
    }
}
