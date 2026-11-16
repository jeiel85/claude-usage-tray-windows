package io.github.jeiel85.claudeusage.push

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.io.File

/**
 * Account fields on a push (#262): the PC's fixture carries the account, the notification names the account
 * instead of the PC, and alerts without one (weather, tests, older PCs) keep showing the PC.
 */
class PushMessageAccountTest {

    private fun fixture(name: String): String {
        val candidates = listOf(File("../../docs/mobile-protocol/$name"), File("docs/mobile-protocol/$name"))
        return candidates.first { it.exists() }.readText(Charsets.UTF_8)
    }

    @Test
    fun fixtureCarriesTheAccount() {
        val m = PushMessage.parse(fixture("push-message.v1.json"))
        assertEquals("claude", m.provider)
        assertEquals(64, m.accountHash?.length)
        assertEquals("김민준 · Acme Corp", m.accountLabel)
        assertEquals("김민준 · Acme Corp", m.source)
    }

    @Test
    fun withoutAnAccountTheSendingPcIsShown() {
        val old = """{"schema":1,"id":"x","pcId":"pc","pcName":"HOME-PC","category":"weather","priority":"info",
            |"title":"Rain","ts":"2026-11-14T03:00:00+00:00"}""".trimMargin()
        val m = PushMessage.parse(old)
        assertNull(m.provider)
        assertNull(m.accountHash)
        assertNull(m.accountLabel)
        assertEquals("HOME-PC", m.source)

        // Codex: the PC sends the account hash but no display name.
        val codex = PushMessage.parse(old.replace("\"title\"", "\"provider\":\"Codex\",\"accountHash\":\"ab\",\"accountLabel\":\"  \",\"title\""))
        assertEquals("codex", codex.provider)
        assertNull(codex.accountLabel)
        assertEquals("HOME-PC", codex.source)
    }

    @Test
    fun historyKeepsTheAccountName() {
        val m = PushMessage.parse(fixture("push-message.v1.json"))
        val entry = NotificationHistoryLog.fromMessage(m, receivedAtMs = 1L)
        val restored = NotificationHistoryLog.parse(NotificationHistoryLog.serialize(listOf(entry))).single()
        assertEquals("김민준 · Acme Corp", restored.source)

        // Rows stored before #262 have no "acct": they still show the PC.
        val legacy = """[{"id":"a","pc":"Desk","cat":"usage","prio":"info","title":"t","body":"","url":"","at":1,"rcv":1}]"""
        assertEquals("Desk", NotificationHistoryLog.parse(legacy).single().source)
    }
}
