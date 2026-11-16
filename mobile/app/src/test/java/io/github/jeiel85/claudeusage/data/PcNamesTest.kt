package io.github.jeiel85.claudeusage.data

import io.github.jeiel85.claudeusage.net.Endpoint
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/** PC names (#271): the PC's own name and this phone's alias are cleaned the same way the PC cleans its setting. */
class PcNamesTest {

    @Test
    fun blankNamesAreNull() {
        assertNull(PcNames.clean(null))
        assertNull(PcNames.clean(""))
        assertNull(PcNames.clean(" \n\t "))
    }

    @Test
    fun foldsWhitespaceAndControlCharactersIntoOneLine() {
        assertEquals("Home PC", PcNames.clean("  Home\r\n\tPC  "))
        assertEquals("a b", PcNames.clean("a\u0007b"))
        assertEquals("거실 데스크톱", PcNames.clean("거실   데스크톱"))
    }

    @Test
    fun cutsAtTheLimitWithoutSplittingAnEmoji() {
        assertEquals("x".repeat(PcNames.MAX_ALIAS), PcNames.clean("x".repeat(50), PcNames.MAX_ALIAS))
        val name = "x".repeat(31) + "💻"
        assertEquals("x".repeat(31), PcNames.clean(name, PcNames.MAX_ALIAS))
        assertEquals(name, PcNames.clean(name, 33))
    }

    @Test
    fun aliasWinsOverThePcsNameUntilCleared() {
        val pc = PcEntry("pc", "DESKTOP-1", listOf(Endpoint(Endpoint.LAN, "192.168.0.10", 47830, true)), "dev", "token")
        assertEquals("DESKTOP-1", pc.displayName)
        assertEquals("Study", pc.copy(alias = "Study").displayName)
        assertEquals("Renamed", pc.copy(name = "Renamed").displayName)
    }
}
