package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NotificationHistoryLogTest {
    private val day = 24 * 3600 * 1000L
    private val now = 100 * day

    private fun entry(id: String, atMs: Long, receivedAtMs: Long = atMs, url: String? = null) = HistoryEntry(
        id = id, pcName = "Desk", category = "usage", priority = "warning",
        title = "Claude 5h 90%", body = "body $id", clickUrl = url, atMs = atMs, receivedAtMs = receivedAtMs,
    )

    @Test
    fun newestFirstByEventTime() {
        var list = emptyList<HistoryEntry>()
        list = NotificationHistoryLog.add(list, entry("a", now - 3000), now)
        list = NotificationHistoryLog.add(list, entry("b", now - 1000), now)
        // Delivered late: event older than "b" although received last.
        list = NotificationHistoryLog.add(list, entry("c", now - 2000, receivedAtMs = now), now)
        assertEquals(listOf("b", "c", "a"), list.map { it.id })
    }

    @Test
    fun sameIdIsKeptOnce() {
        var list = NotificationHistoryLog.add(emptyList(), entry("a", now - 1000), now)
        list = NotificationHistoryLog.add(list, entry("a", now - 1000, receivedAtMs = now), now)
        assertEquals(1, list.size)
    }

    @Test
    fun blankIdsAreNotMerged() {
        var list = NotificationHistoryLog.add(emptyList(), entry("", now - 1000), now)
        list = NotificationHistoryLog.add(list, entry("", now - 500), now)
        assertEquals(2, list.size)
    }

    @Test
    fun dropsEntriesOlderThan30DaysAndCapsCount() {
        val old = entry("old", now - 31 * day)
        var list = NotificationHistoryLog.add(listOf(old), entry("new", now), now)
        assertEquals(listOf("new"), list.map { it.id })

        list = emptyList()
        repeat(NotificationHistoryLog.MAX_ENTRIES + 5) { i -> list = NotificationHistoryLog.add(list, entry("e$i", now - 10_000 + i), now) }
        assertEquals(NotificationHistoryLog.MAX_ENTRIES, list.size)
        assertEquals("e${NotificationHistoryLog.MAX_ENTRIES + 4}", list.first().id) // the oldest ones go
    }

    @Test
    fun unreadCountsByReceiveTime() {
        val list = listOf(entry("a", now - 5000, receivedAtMs = now - 5000), entry("b", now - 9000, receivedAtMs = now))
        assertEquals(1, NotificationHistoryLog.unreadCount(list, lastSeenMs = now - 1000))
        assertEquals(0, NotificationHistoryLog.unreadCount(list, lastSeenMs = now))
    }

    @Test
    fun jsonRoundTripKeepsFieldsAndSkipsBadRows() {
        val list = listOf(entry("a", now, url = "https://example.com/f"), entry("b", now - 1).copy(body = null))
        val back = NotificationHistoryLog.parse(NotificationHistoryLog.serialize(list))
        assertEquals(list, back)

        val mixed = """[{"id":"x","title":""},{"id":"y","title":"ok","url":"http://insecure","at":5,"rcv":6},7]"""
        val parsed = NotificationHistoryLog.parse(mixed)
        assertEquals(listOf("y"), parsed.map { it.id })
        assertNull(parsed.single().clickUrl)
        assertTrue(NotificationHistoryLog.parse("not json").isEmpty())
    }

    @Test
    fun fromMessageUsesEventTimeAndReceiveTime() {
        val m = PushMessage(
            id = "m1", pcId = "pc", pcName = "Desk", category = "weather", priority = "info", title = "Rain",
            body = null, eventKey = null, eventToleranceSec = 0, clickUrl = "https://w", tsMs = 42L,
        )
        val e = NotificationHistoryLog.fromMessage(m, receivedAtMs = 99L)
        assertEquals(42L, e.atMs)
        assertEquals(99L, e.receivedAtMs)
        assertEquals("weather", e.category)
        assertEquals("https://w", e.clickUrl)
    }
}
