package io.github.jeiel85.claudeusage.domain

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * "Try with sample data" (#261) has to look like the real home screen: the same aggregation turns the made-up PCs
 * into account cards, and nothing in them could reach a real PC.
 */
class SampleDataTest {

    @Test
    fun aggregatesIntoTheCardsTheHomeScreenShows() {
        val now = System.currentTimeMillis()
        val status = SampleData.status(now)
        val cards = AccountAggregator.aggregate(status.values.map { it.toView() }, now)

        assertEquals(listOf("claude", "codex", "antigravity"), cards.map { it.provider })
        // One Claude account on two PCs → one card listing both, today's tokens summed only for today's snapshots.
        val claude = cards.first()
        assertEquals(2, claude.devices.size)
        assertTrue(claude.gauges.isNotEmpty())
        assertEquals(1, status.values.count { it.connection == PcConnection.LIVE })
    }

    @Test
    fun carriesNoCredentialsOrRealIdentity() {
        val status = SampleData.status(System.currentTimeMillis())
        for (pc in status.values) {
            assertEquals("", pc.pc.token)
            assertTrue(pc.pc.pcId.startsWith("sample-"))
            pc.snapshot?.providers?.forEach { assertTrue("no email in labels", '@' !in it.accountLabel) }
        }
    }
}
