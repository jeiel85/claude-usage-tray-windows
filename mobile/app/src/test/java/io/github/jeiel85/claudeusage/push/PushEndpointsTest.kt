package io.github.jeiel85.claudeusage.push

import io.github.jeiel85.claudeusage.net.Endpoint
import io.github.jeiel85.claudeusage.net.ProtocolException
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.io.File
import java.time.OffsetDateTime

/**
 * The PC's signed path push (#225, fixture `push-endpoints.v1.json` written by the PC's
 * CloudflareTunnelTests): the MAC must verify with this phone's token for that PC, and nothing else.
 */
class PushEndpointsTest {

    /** Same value as `MobileProtocolFixtures.DeviceToken` on the PC side. */
    private val token = "token-for-push-endpoints-fixture-0123456789abcdef"
    private val pcId = "0123456789abcdef0123456789abcdef"

    private fun fixture(name: String): String {
        val candidates = listOf(File("../../docs/mobile-protocol/$name"), File("docs/mobile-protocol/$name"))
        return candidates.first { it.exists() }.readText(Charsets.UTF_8)
    }

    @Test
    fun fixture_verifies_with_the_device_token() {
        val json = fixture("push-endpoints.v1.json")
        assertEquals(PushEndpointsUpdate.KIND, PushEndpointsUpdate.kindOf(json))
        assertEquals(pcId, PushEndpointsUpdate.pcIdOf(json))
        val update = PushEndpointsUpdate.verify(json, token)
        assertEquals(pcId, update.pcId)
        assertEquals(OffsetDateTime.parse("2026-11-14T03:00:00+00:00").toInstant().toEpochMilli(), update.tsEpochMillis)
        assertEquals(
            listOf(Endpoint.LAN, Endpoint.TAILSCALE, Endpoint.TAILSCALE, Endpoint.CLOUDFLARE),
            update.endpoints.map { it.transport },
        )
    }

    @Test(expected = ProtocolException::class)
    fun another_pcs_token_does_not_verify() {
        PushEndpointsUpdate.verify(fixture("push-endpoints.v1.json"), token + "x")
    }

    @Test(expected = ProtocolException::class)
    fun a_rewritten_path_list_does_not_verify() {
        val json = fixture("push-endpoints.v1.json")
        val o = org.json.JSONObject(json)
        val data = String(WebPush.unb64url(o.getString("data")), Charsets.UTF_8)
            .replace("bright-fox-quiet-river.trycloudflare.com", "evil.trycloudflare.com")
        o.put("data", WebPush.b64url(data.toByteArray(Charsets.UTF_8)))
        PushEndpointsUpdate.verify(o.toString(), token)
    }

    @Test
    fun a_notification_is_not_a_path_push() {
        assertNull(PushEndpointsUpdate.kindOf(fixture("push-message.v1.json")))
        assertNull(PushEndpointsUpdate.kindOf("not json"))
    }

    @Test
    fun only_a_newer_push_replaces_the_paths() {
        val cf = Endpoint(Endpoint.CLOUDFLARE, "new.trycloudflare.com", 443, true)
        assertTrue(PushEndpointsUpdate(pcId, 2_000L, listOf(cf)).isNewerThan(1_000L))
        assertFalse(PushEndpointsUpdate(pcId, 1_000L, listOf(cf)).isNewerThan(1_000L)) // late duplicate
        assertFalse(PushEndpointsUpdate(pcId, 500L, listOf(cf)).isNewerThan(1_000L))
        assertFalse(PushEndpointsUpdate(pcId, 2_000L, emptyList()).isNewerThan(1_000L))
    }
}
