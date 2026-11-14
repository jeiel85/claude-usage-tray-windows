package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** A distributor registration this app no longer holds is unregistered; ours never is (#242). */
class StaleTokenTest {

    private val current = "11111111-1111-1111-1111-111111111111"
    private val old = "22222222-2222-2222-2222-222222222222"

    @Test
    fun message_for_an_old_token_is_stale() {
        assertEquals(old, UnifiedPush.staleToken(UnifiedPush.ACTION_MESSAGE, old, current))
    }

    @Test
    fun new_endpoint_for_an_old_token_is_stale() {
        assertEquals(old, UnifiedPush.staleToken(UnifiedPush.ACTION_NEW_ENDPOINT, old, current))
    }

    @Test
    fun any_token_is_stale_after_push_was_turned_off_or_data_wiped() {
        assertEquals(old, UnifiedPush.staleToken(UnifiedPush.ACTION_MESSAGE, old, null))
    }

    @Test
    fun the_current_token_is_never_dropped() {
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_MESSAGE, current, current))
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_NEW_ENDPOINT, current, current))
    }

    @Test
    fun broadcasts_saying_the_registration_is_gone_are_not_answered() {
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_UNREGISTERED, old, current))
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_REGISTRATION_FAILED, old, current))
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_TEMP_UNAVAILABLE, old, current))
    }

    @Test
    fun missing_or_blank_token_is_ignored() {
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_MESSAGE, null, current))
        assertNull(UnifiedPush.staleToken(UnifiedPush.ACTION_MESSAGE, " ", current))
    }
}
