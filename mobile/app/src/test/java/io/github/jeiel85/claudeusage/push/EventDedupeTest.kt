package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** The same alert from several PCs is shown once; a new event (outside the tolerance) still shows (#222). */
class EventDedupeTest {

    private val now = 1_800_000_000_000L

    @Test
    fun same_event_from_another_pc_within_tolerance_is_dropped() {
        val (first, json) = EventDedupe.check(null, "claude-usage-short-90-abc_1794620400", 300, now)
        assertTrue(first)
        // Another PC detected it two minutes later: different floored epoch, same event.
        val (second, _) = EventDedupe.check(json, "claude-usage-short-90-abc_1794620520", 300, now + 1000)
        assertFalse(second)
    }

    @Test
    fun a_new_window_is_a_new_event() {
        val (_, json) = EventDedupe.check(null, "claude-usage-short-90-abc_1794620400", 300, now)
        val (show, _) = EventDedupe.check(json, "claude-usage-short-90-abc_1794638400", 300, now + 1000)
        assertTrue(show)
    }

    @Test
    fun exact_keys_and_keyless_messages() {
        val (_, json) = EventDedupe.check(null, "weather-1a2b3c", 0, now)
        assertFalse(EventDedupe.check(json, "weather-1a2b3c", 0, now).first)
        assertTrue(EventDedupe.check(json, "weather-9z9z9z", 0, now).first)
        // Tests carry no key and always show.
        assertTrue(EventDedupe.check(json, null, 0, now).first)
        assertTrue(EventDedupe.check(json, null, 0, now).first)
    }

    @Test
    fun remembered_events_expire_after_a_day() {
        val (_, json) = EventDedupe.check(null, "weather-1a2b3c", 0, now)
        assertTrue(EventDedupe.check(json, "weather-1a2b3c", 0, now + EventDedupe.KEEP_MS + 1).first)
    }

    @Test
    fun a_different_account_prefix_is_not_a_duplicate() {
        val (_, json) = EventDedupe.check(null, "claude-usage-short-90-abc_1794620400", 300, now)
        assertTrue(EventDedupe.check(json, "claude-usage-short-90-xyz_1794620400", 300, now).first)
    }
}
