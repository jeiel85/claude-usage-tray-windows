package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/** Only what needs the user now sounds and vibrates; old per-category channels are found and replaced (#297). */
class AlertUrgencyTest {

    @Test
    fun warning_and_critical_are_urgent() {
        assertTrue(NotificationGroups.isUrgent("warning"))
        assertTrue(NotificationGroups.isUrgent("critical"))
    }

    @Test
    fun info_and_unknown_priorities_are_quiet() {
        assertFalse(NotificationGroups.isUrgent("info"))
        assertFalse(NotificationGroups.isUrgent(""))
        assertFalse(NotificationGroups.isUrgent("WARNING"))
    }

    @Test
    fun per_category_channels_are_legacy() {
        listOf("cut.usage", "cut.exhaustion", "cut.rate_limit", "cut.reset", "cut.weather", "cut.test", "cut.", "cut.future-kind")
            .forEach { assertTrue(it, PushNotifier.isLegacyChannel(it)) }
    }

    @Test
    fun current_and_foreign_channels_are_kept() {
        listOf(PushNotifier.URGENT_CHANNEL, PushNotifier.QUIET_CHANNEL, "cut.app.update", "miscellaneous", "usage")
            .forEach { assertFalse(it, PushNotifier.isLegacyChannel(it)) }
    }
}
