package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertEquals
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

    @Test
    fun blocked_legacy_channels_become_in_app_mutes() {
        val none = android.app.NotificationManager.IMPORTANCE_NONE
        val high = android.app.NotificationManager.IMPORTANCE_HIGH
        val muted = PushNotifier.blockedCategories(
            listOf("cut.weather" to none, "cut.usage" to high, "cut.test" to none, "cut.unknown" to none, "cut.alert.quiet" to none),
        )
        // test can't be muted and unknown kinds have no switch; only real, blocked kinds carry over.
        assertEquals(setOf("weather"), muted)
    }
}

