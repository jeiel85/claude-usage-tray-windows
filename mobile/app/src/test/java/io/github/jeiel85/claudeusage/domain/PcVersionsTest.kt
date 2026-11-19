package io.github.jeiel85.claudeusage.domain

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class PcVersionsTest {

    @Test
    fun pcs_before_the_alert_priority_change_are_outdated() {
        assertEquals("1.68.0", PcVersions.RECOMMENDED_MIN)
        assertTrue(PcVersions.isOutdated("1.67.3"))
        assertTrue(PcVersions.isOutdated("1.9.0"))
        assertTrue(PcVersions.isOutdated("v1.53.0"))
        assertFalse(PcVersions.isOutdated("1.68.0"))
        assertFalse(PcVersions.isOutdated("1.72.5"))
        assertFalse(PcVersions.isOutdated("1.100.0"))
        assertFalse(PcVersions.isOutdated("2.0"))
    }

    @Test
    fun build_and_prerelease_tails_are_ignored() {
        assertTrue(PcVersions.isOutdated("1.67.3+abc123"))
        assertFalse(PcVersions.isOutdated("1.68.0-beta"))
    }

    @Test
    fun unknown_versions_do_not_nag() {
        listOf(null, "", "  ", "dev", "1.x.0", "1.2.3.4.5", "-1.0.0").forEach {
            assertFalse("\"$it\"", PcVersions.isOutdated(it))
        }
    }
}
