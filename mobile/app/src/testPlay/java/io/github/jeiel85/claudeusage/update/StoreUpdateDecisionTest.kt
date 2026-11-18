package io.github.jeiel85.claudeusage.update

import com.google.android.play.core.install.model.UpdateAvailability
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.TimeUnit

/** The Play build's update notice rules (#284). */
class StoreUpdateDecisionTest {

    @Test
    fun offersOnlyAHigherVersionPlayCallsAvailable() {
        assertEquals(
            StoreUpdates.Result.Available(16700),
            StoreUpdateDecision.interpret(UpdateAvailability.UPDATE_AVAILABLE, 16700, installedVersionCode = 16600),
        )
        // A local build newer than the store's, or the same version: nothing to offer.
        assertEquals(StoreUpdates.Result.UpToDate, StoreUpdateDecision.interpret(UpdateAvailability.UPDATE_AVAILABLE, 16600, 16600))
        assertEquals(StoreUpdates.Result.UpToDate, StoreUpdateDecision.interpret(UpdateAvailability.UPDATE_AVAILABLE, 16500, 16600))
        assertEquals(StoreUpdates.Result.UpToDate, StoreUpdateDecision.interpret(UpdateAvailability.UPDATE_NOT_AVAILABLE, 0, 16600))
        assertEquals(StoreUpdates.Result.UpToDate, StoreUpdateDecision.interpret(UpdateAvailability.UNKNOWN, 16700, 16600))
        // Play is already installing it: don't send the user there again.
        assertEquals(
            StoreUpdates.Result.UpToDate,
            StoreUpdateDecision.interpret(UpdateAvailability.DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS, 16700, 16600),
        )
    }

    @Test
    fun notifiesOncePerVersion() {
        assertTrue(StoreUpdateDecision.shouldNotify(16700, notifiedVersionCode = 0))
        assertFalse(StoreUpdateDecision.shouldNotify(16700, notifiedVersionCode = 16700))
        assertTrue(StoreUpdateDecision.shouldNotify(16800, notifiedVersionCode = 16700))
    }

    @Test
    fun rechecksAfterSixHoursOrWhenTheClockWentBack() {
        val now = TimeUnit.DAYS.toMillis(100)
        assertTrue(StoreUpdateDecision.isStale(0L, now))
        assertFalse(StoreUpdateDecision.isStale(now - TimeUnit.HOURS.toMillis(5), now))
        assertTrue(StoreUpdateDecision.isStale(now - TimeUnit.HOURS.toMillis(6), now))
        assertTrue(StoreUpdateDecision.isStale(now + 1, now))
    }

    @Test
    fun versionNameFollowsTheVersionCodeFormula() {
        assertEquals("1.67.0", StoreUpdateDecision.versionName(16700))
        assertEquals("1.65.3", StoreUpdateDecision.versionName(16503))
        assertEquals("2.0.12", StoreUpdateDecision.versionName(20012))
    }
}
