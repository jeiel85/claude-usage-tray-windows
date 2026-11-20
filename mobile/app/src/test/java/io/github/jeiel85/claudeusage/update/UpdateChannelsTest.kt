package io.github.jeiel85.claudeusage.update

import android.app.NotificationManager
import io.github.jeiel85.claudeusage.push.PushNotifier
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotEquals
import org.junit.Test

/** Update notices are silent unless they wait for the user; the old loud channel is replaced (#299). */
class UpdateChannelsTest {
    private val none = NotificationManager.IMPORTANCE_NONE
    private val low = NotificationManager.IMPORTANCE_LOW
    private val default = NotificationManager.IMPORTANCE_DEFAULT

    @Test
    fun new_channels_keep_their_intended_importance() {
        assertEquals(low, UpdateChannels.importanceFor(low, legacyImportance = null))
        assertEquals(default, UpdateChannels.importanceFor(default, legacyImportance = default))
        assertEquals(low, UpdateChannels.importanceFor(low, legacyImportance = NotificationManager.IMPORTANCE_HIGH))
    }

    @Test
    fun a_user_who_turned_the_old_channel_off_keeps_update_notices_off() {
        assertEquals(none, UpdateChannels.importanceFor(low, legacyImportance = none))
        assertEquals(none, UpdateChannels.importanceFor(default, legacyImportance = none))
    }

    @Test
    fun channel_ids_are_new_and_not_mistaken_for_old_alert_channels() {
        // Reusing the old id would keep its importance: Android ignores the importance of an existing channel.
        assertNotEquals(UpdateChannels.LEGACY, UpdateChannels.QUIET)
        assertNotEquals(UpdateChannels.LEGACY, UpdateChannels.ACTION)
        listOf(UpdateChannels.LEGACY, UpdateChannels.QUIET, UpdateChannels.ACTION)
            .forEach { assertFalse(it, PushNotifier.isLegacyChannel(it)) }
    }
}
