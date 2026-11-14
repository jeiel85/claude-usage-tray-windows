package io.github.jeiel85.claudeusage.net

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.Base64

class PairingLinkTest {

    private val payload = Base64.getUrlEncoder().withoutPadding().encodeToString("""{"cu":1}""".toByteArray())

    @Test
    fun reads_the_web_link_and_the_app_scheme() {
        assertEquals("""{"cu":1}""", PairingLink.decode("${PairingLink.WEB}/#$payload"))
        assertEquals("""{"cu":1}""", PairingLink.decode("${PairingLink.APP}$payload"))
        assertTrue(PairingLink.isLink("${PairingLink.WEB}/#$payload"))
    }

    @Test
    fun other_text_is_not_a_link() {
        assertFalse(PairingLink.isLink("https://example.com/#$payload"))
        assertNull(PairingLink.decode("${PairingLink.WEB}/"))
        assertNull(PairingLink.decode("${PairingLink.APP}!!!"))
    }
}
