package io.github.jeiel85.claudeusage.domain

import io.github.jeiel85.claudeusage.data.PcEntry
import io.github.jeiel85.claudeusage.net.Endpoint
import io.github.jeiel85.claudeusage.net.Gauge
import io.github.jeiel85.claudeusage.net.GaugeKind
import io.github.jeiel85.claudeusage.net.ProviderUsage
import io.github.jeiel85.claudeusage.net.Quota
import io.github.jeiel85.claudeusage.net.TodayTotals
import io.github.jeiel85.claudeusage.net.UsageSnapshot
import java.time.LocalDate

/**
 * Made-up PCs for "Try with sample data" on the empty home screen (#261) and the debug design preview: three PCs
 * (live, retrying, offline) with Claude, Codex and Antigravity usage. Nothing here is stored or sent — the
 * entries carry no real address or token, and the screens only render them.
 *
 * The account label has the shape the PC really sends ("name · team org", never an email — MOBILE_PROTOCOL.md);
 * the Play store screenshots are taken from this, so no real names. Same account as the debug preview's sample alerts.
 */
object SampleData {
    fun status(now: Long): Map<String, PcStatus> {
        val today = LocalDate.now().toString()
        fun claude(observed: Long, short: Double, long: Double) = ProviderUsage(
            provider = "claude", accountHash = "a".repeat(64), accountScope = ProviderUsage.SCOPE_ACCOUNT,
            accountLabel = "김민준 · Acme Corp", observedAtMs = observed, errorKind = "",
            quota = Quota(
                hasData = true, observedAtMs = observed, planType = "max",
                gauges = listOf(
                    Gauge(GaugeKind.SHORT, "", short, now + 2 * HOUR + 13 * MINUTE, 300),
                    Gauge(GaugeKind.LONG, "", long, now + 3 * DAY + 4 * HOUR, 7 * 24 * 60),
                    Gauge(GaugeKind.NAMED, "Opus", 0.94, now + 3 * DAY + 4 * HOUR, 7 * 24 * 60),
                ),
                extraCreditsLabel = "",
            ),
            today = TodayTotals(today, 182_000, 64_000, 2_310_000, 140_000, 6, 220),
        )
        val codex = ProviderUsage(
            provider = "codex", accountHash = "b".repeat(64), accountScope = ProviderUsage.SCOPE_ACCOUNT,
            accountLabel = "", observedAtMs = now - MINUTE, errorKind = "",
            quota = Quota(
                hasData = true, observedAtMs = now - MINUTE, planType = "plus",
                gauges = listOf(
                    Gauge(GaugeKind.SHORT, "", 0.12, now + 4 * HOUR, 300),
                    Gauge(GaugeKind.LONG, "", 0.48, now + 5 * DAY, 7 * 24 * 60),
                ),
                extraCreditsLabel = "",
            ),
            today = TodayTotals(today, 40_000, 9_000, 310_000, 0, 2, 41),
        )
        val antigravity = ProviderUsage(
            provider = "antigravity", accountHash = "c".repeat(64), accountScope = ProviderUsage.SCOPE_DEVICE,
            accountLabel = "", observedAtMs = now - 2 * MINUTE, errorKind = "",
            quota = Quota(
                hasData = true, observedAtMs = now - 2 * MINUTE, planType = "Google AI Pro",
                gauges = listOf(
                    Gauge(
                        kind = GaugeKind.NAMED, name = "Gemini Models · 5h", usage = 0.21,
                        resetAtMs = now + 4 * HOUR, windowMinutes = 300,
                        group = "Gemini Models",
                        groupDescription = "Gemini 4 Neon, Gemini 4 Carbon, Gemini 4 Argon, Gemini 4 Hydrogen, Gemini 3.8 Flash",
                        bucketId = "gemini-5h",
                    ),
                    Gauge(
                        kind = GaugeKind.NAMED, name = "Gemini Models · 7d", usage = 0.06,
                        resetAtMs = now + 6 * DAY, windowMinutes = 7 * 24 * 60,
                        group = "Gemini Models",
                        groupDescription = "Gemini 4 Neon, Gemini 4 Carbon, Gemini 4 Argon, Gemini 4 Hydrogen, Gemini 3.8 Flash",
                        bucketId = "gemini-weekly",
                    ),
                ),
                extraCreditsLabel = "",
                aiCredits = 250L,
                aiCreditsMinimum = 50L,
            ),
            today = null,
        )
        fun pc(id: String, name: String) = PcEntry(id, name, listOf(Endpoint(Endpoint.LAN, "192.168.0.10", 47830, true)), "sample", "")
        val desk = pc("sample-desk", "DESKTOP-STUDIO")
        val laptop = pc("sample-laptop", "LAPTOP-TRAVEL")
        val office = pc("sample-office", "OFFICE-WS")
        return mapOf(
            desk.pcId to PcStatus(
                desk, PcConnection.LIVE, "LAN 192.168.0.10", null, null,
                UsageSnapshot(desk.pcId, desk.name, "1.61.1", now, listOf(claude(now - 30_000, 0.73, 0.41), codex, antigravity)),
                now - 30_000,
            ),
            laptop.pcId to PcStatus(
                laptop, PcConnection.RETRYING, null, "connect timed out", 12,
                UsageSnapshot(laptop.pcId, laptop.name, "1.61.1", now - 2 * HOUR, listOf(claude(now - 2 * HOUR, 0.6, 0.38))),
                now - 2 * HOUR,
            ),
            office.pcId to PcStatus(office, PcConnection.OFFLINE, null, null, null, null, null),
        )
    }

    private const val MINUTE = 60_000L
    private const val HOUR = 60 * MINUTE
    private const val DAY = 24 * HOUR
}
