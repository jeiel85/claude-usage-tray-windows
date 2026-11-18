package io.github.jeiel85.claudeusage.net

import io.github.jeiel85.claudeusage.data.PcEntry
import kotlinx.coroutines.runBlocking
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Assert.fail
import org.junit.Before
import org.junit.Test
import java.io.IOException

/**
 * Removing a PC on the phone also removes the phone on the PC (#285), so re-pairing doesn't list it twice.
 * Older PCs don't know `DELETE /api/v1/pair` and get the push-only removal they always got.
 */
class ForgetPcTest {

    private lateinit var server: MockWebServer
    private val api = UsageApi()

    @Before
    fun start() {
        server = MockWebServer().apply { start() }
    }

    @After
    fun stop() {
        server.shutdown()
    }

    private fun pc(vararg endpoints: Endpoint) =
        PcEntry("pc-1", "HOME-PC", endpoints.toList(), "dev-1", "secret-token")

    private val here get() = Endpoint(Endpoint.LOOPBACK, "127.0.0.1", server.port, tls = false)

    private fun json(code: Int) = MockResponse().setResponseCode(code).setBody("{}")

    @Test
    fun current_pc_drops_the_pairing_and_needs_nothing_else() = runBlocking {
        server.enqueue(json(200))
        assertEquals(UnpairOutcome.Unpaired, api.forget(pc(here)))

        val req = server.takeRequest()
        assertEquals("DELETE", req.method)
        assertEquals("/api/v1/pair", req.path)
        assertEquals("Bearer secret-token", req.getHeader("Authorization"))
        assertEquals(1, server.requestCount)
    }

    @Test
    fun older_pc_without_the_route_still_stops_its_pushes() = runBlocking {
        server.enqueue(json(405)) // MobileApiServer before #285: method_not_allowed for DELETE /api/v1/pair
        server.enqueue(MockResponse().setBody("""{"schema":1,"deviceId":"dev-1","registered":false}"""))
        assertEquals(UnpairOutcome.Unsupported, api.forget(pc(here)))

        assertEquals("/api/v1/pair", server.takeRequest().path)
        val push = server.takeRequest()
        assertEquals("DELETE", push.method)
        assertEquals("/api/v1/push", push.path)
    }

    @Test
    fun pc_that_already_revoked_the_phone_is_left_alone() = runBlocking {
        server.enqueue(json(401))
        assertEquals(UnpairOutcome.AlreadyGone, api.forget(pc(here)))
        assertEquals(1, server.requestCount)
    }

    @Test
    fun dead_path_falls_through_to_the_next_one() = runBlocking {
        val dead = MockWebServer().apply { start() }
        val deadEndpoint = Endpoint(Endpoint.LAN, "127.0.0.1", dead.port, tls = false)
        dead.shutdown() // connection refused
        server.enqueue(json(200))
        assertEquals(UnpairOutcome.Unpaired, api.forget(pc(deadEndpoint, here)))
    }

    @Test
    fun unreachable_pc_is_an_io_error() {
        val dead = MockWebServer().apply { start() }
        val deadEndpoint = Endpoint(Endpoint.LAN, "127.0.0.1", dead.port, tls = false)
        dead.shutdown()
        try {
            runBlocking { api.forget(pc(deadEndpoint)) }
            fail("expected IOException")
        } catch (e: IOException) {
            assertTrue(e.message!!.startsWith("LAN:"))
        }
    }

    @Test
    fun status_mapping() {
        assertEquals(UnpairOutcome.Unpaired, UnpairOutcome.fromStatus(200))
        assertEquals(UnpairOutcome.AlreadyGone, UnpairOutcome.fromStatus(401))
        assertEquals(UnpairOutcome.Unsupported, UnpairOutcome.fromStatus(404))
        assertEquals(UnpairOutcome.Unsupported, UnpairOutcome.fromStatus(405))
        assertNull(UnpairOutcome.fromStatus(500))
    }
}
