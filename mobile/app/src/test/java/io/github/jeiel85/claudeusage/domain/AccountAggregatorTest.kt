package io.github.jeiel85.claudeusage.domain

import io.github.jeiel85.claudeusage.net.Gauge
import io.github.jeiel85.claudeusage.net.GaugeKind
import io.github.jeiel85.claudeusage.net.ProviderUsage
import io.github.jeiel85.claudeusage.net.Quota
import io.github.jeiel85.claudeusage.net.TodayTotals
import io.github.jeiel85.claudeusage.net.UsageSnapshot
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The requirement from #220: three PCs, two on account A and one on account B → two account cards,
 * the first listing both PCs. Quota is account-level (freshest wins), tokens are per PC (summed).
 */
class AccountAggregatorTest {

    private val now = 10_000_000L
    private val hashA = "a".repeat(64)
    private val hashB = "b".repeat(64)

    private fun quota(usage: Double, observedAt: Long, hasData: Boolean = true) = Quota(
        hasData = hasData,
        observedAtMs = observedAt,
        planType = "max",
        gauges = listOf(Gauge(GaugeKind.SHORT, "", usage, now + 3_600_000, 300)),
        extraCreditsLabel = "",
    )

    private fun usage(
        hash: String,
        observedAt: Long,
        quota: Quota?,
        tokens: Long = 0,
        scope: String = ProviderUsage.SCOPE_ACCOUNT,
        provider: String = "claude",
        label: String = "",
    ) = ProviderUsage(
        provider, hash, scope, label, observedAt, "", quota,
        if (tokens > 0) TodayTotals(tokens, 0, 0, 0, 1, 1) else null,
    )

    private fun pc(id: String, name: String, vararg providers: ProviderUsage, online: Boolean = true) =
        PcView(id, name, online, UsageSnapshot(id, name, "1.53.0", now, providers.toList()), now)

    @Test
    fun three_pcs_two_accounts_make_two_cards() {
        val cards = AccountAggregator.aggregate(
            listOf(
                pc("p1", "HOME", usage(hashA, now - 60_000, quota(0.30, now - 60_000), tokens = 100, label = "Me")),
                pc("p2", "WORK", usage(hashA, now - 10_000, quota(0.35, now - 10_000), tokens = 50)),
                pc("p3", "LAPTOP", usage(hashB, now - 5_000, quota(0.80, now - 5_000), tokens = 7)),
            ),
            now,
        )
        assertEquals(2, cards.size)
        val a = cards.first { it.key.endsWith(hashA) }
        assertEquals(listOf("HOME", "WORK"), a.devices.map { it.pcName })
        // Account-level quota: the freshest PC's value, not a sum.
        assertEquals(0.35, a.gauges.single().usage, 1e-9)
        assertEquals("WORK", a.quotaFromPcName)
        // Device-level tokens: summed.
        assertEquals(150L, a.today!!.inputTokens)
        assertEquals("Me", a.label)
        val b = cards.first { it.key.endsWith(hashB) }
        assertEquals(listOf("LAPTOP"), b.devices.map { it.pcName })
    }

    @Test
    fun a_pc_without_data_does_not_blank_out_another_pcs_quota() {
        val cards = AccountAggregator.aggregate(
            listOf(
                pc("p1", "HOME", usage(hashA, now - 120_000, quota(0.5, now - 120_000))),
                pc("p2", "WORK", usage(hashA, now - 1_000, quota(0.0, now - 1_000, hasData = false))),
            ),
            now,
        )
        assertEquals(0.5, cards.single().gauges.single().usage, 1e-9)
        assertEquals("HOME", cards.single().quotaFromPcName)
    }

    @Test
    fun unknown_accounts_stay_separate_per_pc() {
        val cards = AccountAggregator.aggregate(
            listOf(
                pc("p1", "HOME", usage("c".repeat(64), now, null, scope = ProviderUsage.SCOPE_DEVICE, provider = "antigravity")),
                pc("p2", "WORK", usage("d".repeat(64), now, null, scope = ProviderUsage.SCOPE_DEVICE, provider = "antigravity")),
            ),
            now,
        )
        assertEquals(2, cards.size)
        assertTrue(cards.none { it.accountScoped })
        assertNull(cards[0].quota)
    }

    @Test
    fun providers_are_ordered_claude_codex_opencode_antigravity() {
        val cards = AccountAggregator.aggregate(
            listOf(
                pc(
                    "p1", "HOME",
                    usage("1".repeat(64), now, null, provider = "antigravity"),
                    usage("2".repeat(64), now, null, provider = "codex"),
                    usage("3".repeat(64), now, null, provider = "claude"),
                ),
            ),
            now,
        )
        assertEquals(listOf("claude", "codex", "antigravity"), cards.map { it.provider })
    }

    @Test
    fun passed_reset_is_shown_as_zero() {
        val expired = Quota(true, now - 10, "", listOf(Gauge(GaugeKind.SHORT, "", 0.9, now - 1, 300)), "")
        val card = AccountAggregator.aggregate(listOf(pc("p1", "HOME", usage(hashA, now, expired))), now).single()
        assertEquals(0.0, card.gauges.single().usage, 0.0)
    }
}
