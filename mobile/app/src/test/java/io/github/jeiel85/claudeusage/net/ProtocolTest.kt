package io.github.jeiel85.claudeusage.net

import io.github.jeiel85.claudeusage.domain.AccountAggregator
import io.github.jeiel85.claudeusage.domain.PcView
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

    /**
     * DeepSeek free API (100milionseek, #318): the PC sends its daily allowance as one short window with
     * shortWindowMinutes 1440 — the same JSON HmsMobileFeedTests checks on the PC side.
     */
    @Test
    fun deepseek_daily_allowance_parses_as_one_day_window() {
        val s = UsageSnapshot.parse(
            """
            {"schema":1,"ts":"2026-11-14T03:00:00+00:00","pcId":"p","pcName":"HOME-PC","appVersion":"1.72.0",
             "providers":[{"provider":"100milionseek","accountHash":"${"c".repeat(64)}","accountScope":"account",
              "accountLabel":"","observedAt":"2026-11-14T03:00:00+00:00","errorKind":"",
              "quota":{"hasData":true,"observedAtUtc":"2026-11-14T03:00:00+00:00","shortUsagePercent":0.25,
               "shortResetAt":"2026-11-15T00:00:00+00:00","isShortResetEstimated":false,"longUsagePercent":0,
               "extraUsageEnabled":false,"extraHasLimit":false,"extraCreditsLabel":"","planType":"",
               "shortWindowMinutes":1440,"hasLongWindow":false,"tierName":"","paidTierName":"",
               "models":[],"additionalQuotas":[]}}]}
            """.trimIndent(),
        )
        val deepseek = s.providers.single()
        assertEquals("100milionseek", deepseek.provider)
        val gauge = deepseek.quota!!.gauges.single()
        assertEquals(GaugeKind.SHORT, gauge.kind)
        assertEquals(0.25, gauge.usage, 1e-9)
        assertEquals(24 * 60, gauge.windowMinutes)
        assertEquals(ms("2026-11-15T00:00:00+00:00"), gauge.resetAtMs)
    }

    /**
     * The PC→phone path for real (#320): usage-snapshot.served.v1.json is the first /api/v1/live frame of a paired
     * PC server whose feed got its values from the PC's own producers (MobileServedSnapshotTests). Parsing it and
     * grouping it into account cards is what the Accounts tab does with a live frame — so a new provider that the PC
     * starts sending is checked to become a card without pairing a phone.
     */
    @Test
    fun served_live_frame_becomes_account_cards() {
        val s = UsageSnapshot.parse(fixture("usage-snapshot.served.v1.json"))
        assertEquals("HOME-PC", s.pcName)
        val now = ms("2026-11-14T03:00:00+00:00")
        val cards = AccountAggregator.aggregate(
            listOf(PcView(s.pcId, s.pcName, true, s, now)), now, todayDate = "2026-11-14",
        )
        assertEquals(listOf("claude", "100milionseek"), cards.map { it.provider })
        assertTrue(cards.all(AccountAggregator::hasUsage))

        val claude = cards[0]
        assertEquals("김민준 · Acme Corp", claude.label)
        assertEquals("max", claude.planType)
        assertEquals(listOf(GaugeKind.SHORT, GaugeKind.LONG), claude.gauges.map { it.kind })
        assertEquals(2_734_000L, claude.today?.totalTokens)

        val deepseek = cards[1]
        assertTrue(deepseek.accountScoped)
        assertTrue(AccountAggregator.authFailedOn(deepseek).isEmpty())
        val gauge = deepseek.gauges.single()
        assertEquals(0.25, gauge.usage, 1e-9)
        assertEquals(24 * 60, gauge.windowMinutes)
        assertEquals(ms("2026-11-15T00:00:00+00:00"), gauge.resetAtMs)
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

    // --- 시간선(#269): PC 의 UsageCalculator.TimeProgress 와 같은 위치, 모르면 그리지 않는다 ---

    @Test
    fun time_progress_is_the_elapsed_share_of_the_window() {
        val hour = 3_600_000L
        // 5시간 창이 2시간 남았으면 3/5 경과.
        val g = Gauge(GaugeKind.SHORT, "", 0.4, resetAtMs = 10 * hour, windowMinutes = 300)
        assertEquals(0.6, g.timeProgress(nowMs = 8 * hour)!!, 1e-9)
        assertEquals(0.0, g.timeProgress(nowMs = 5 * hour)!!, 1e-9)
        assertEquals(1.0, g.timeProgress(nowMs = 10 * hour)!!, 1e-9)
    }

    @Test
    fun time_progress_is_not_guessed_outside_the_window_or_without_its_length() {
        val hour = 3_600_000L
        val g = Gauge(GaugeKind.SHORT, "", 0.4, resetAtMs = 10 * hour, windowMinutes = 300)
        assertNull(g.timeProgress(nowMs = 11 * hour)) // 이미 초기화됨
        assertNull(g.timeProgress(nowMs = 4 * hour)) // 초기화가 창 길이보다 멀다 — 창 길이가 어긋난 값
        assertNull(g.copy(windowMinutes = null).timeProgress(nowMs = 8 * hour))
        assertNull(g.copy(windowMinutes = 0).timeProgress(nowMs = 8 * hour))
        assertNull(g.copy(resetAtMs = null).timeProgress(nowMs = 8 * hour))
        // 초기화가 지난 창은 effective() 가 초기화 시각을 지워 마커도 사라진다.
        assertNull(g.effective(nowMs = 11 * hour).timeProgress(nowMs = 11 * hour))
    }

    @Test
    fun opencode_windows_get_their_lengths() {
        val o = org.json.JSONObject(
            """{"hasData":true,"openCode":{
                "rolling":{"usagePercent":0.1,"resetAt":"2026-11-16T05:00:00+09:00"},
                "weekly":{"usagePercent":0.2,"resetAt":"2026-11-20T00:00:00+09:00"},
                "monthly":{"usagePercent":0.3,"resetAt":"2026-12-01T00:00:00+09:00"}}}""",
        )
        val gauges = Quota.parse(o, "opencode")!!.gauges
        assertEquals(listOf("rolling", "weekly", "monthly"), gauges.map { it.name })
        // 월간은 초기화 한 달 전부터 — 11월은 30일.
        assertEquals(listOf(300, 7 * 24 * 60, 30 * 24 * 60), gauges.map { it.windowMinutes })
    }

    @Test
    fun antigravity_windows_come_from_the_window_name_or_the_bucket_id() {
        val o = org.json.JSONObject(
            """{"hasData":true,"models":[
                {"displayName":"Gemini","window":"weekly","remainingFraction":0.5,"resetAt":"2026-11-20T00:00:00+00:00"},
                {"displayName":"Gemini","window":"5h","remainingFraction":0.5,"resetAt":"2026-11-16T05:00:00+00:00"},
                {"displayName":"Claude","modelId":"claude-weekly","remainingFraction":0.5,"resetAt":"2026-11-20T00:00:00+00:00"},
                {"displayName":"Gemini 3 Pro","modelId":"gemini-3-pro","remainingFraction":0.5,"resetAt":"2026-11-16T05:00:00+00:00"}]}""",
        )
        val gauges = Quota.parse(o, "antigravity")!!.gauges
        assertEquals(listOf(7 * 24 * 60, 300, 7 * 24 * 60, null), gauges.map { it.windowMinutes })
    }
}
