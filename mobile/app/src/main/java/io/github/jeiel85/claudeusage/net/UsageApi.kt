package io.github.jeiel85.claudeusage.net

import io.github.jeiel85.claudeusage.data.PcEntry
import io.github.jeiel85.claudeusage.push.PushSubscription
import io.github.jeiel85.claudeusage.push.PushSubscriptionResponse
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.flow
import kotlinx.coroutines.flow.flowOn
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import org.json.JSONObject
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.TimeUnit
import javax.net.ssl.SSLException

/** Pairing failed in a way the user should read; [code] is the PC's error code (invalid_code, code_expired, locked). */
class PairException(val code: String?, val httpStatus: Int) : Exception(code ?: "HTTP $httpStatus")

/** The PC no longer knows this phone (revoked in the tray) — re-pair. */
class UnauthorizedException : Exception("unauthorized")

/** What the app knows about one PC's live connection. */
sealed interface LiveState {
    data object Connecting : LiveState
    data class Live(val snapshot: UsageSnapshot, val json: String, val endpoint: Endpoint) : LiveState
    data class Retrying(val reason: String, val inSeconds: Int) : LiveState
    data object Unauthorized : LiveState
    data class Incompatible(val reason: String) : LiveState
}

/**
 * Direct phone → PC calls, ported from Homebase's `HomebaseApi`. There is no relay server: each PC
 * is reached over one of the paths in its QR — same-LAN TLS with a pinned certificate, or Tailscale —
 * tried in the order [RouteSelector] picks for the phone's current network ([networks]).
 */
class UsageApi(
    private val client: OkHttpClient = defaultClient,
    private val networks: () -> LocalNetworks? = { null },
) {
    private val quick: OkHttpClient = client.newBuilder()
        .connectTimeout(3, TimeUnit.SECONDS)
        .readTimeout(5, TimeUnit.SECONDS)
        .build()

    private val pinnedClients = ConcurrentHashMap<String, OkHttpClient>()

    /** A pinned TLS client for LAN, the normal client otherwise. */
    private fun clientFor(e: Endpoint, base: OkHttpClient): OkHttpClient {
        val pin = e.certSha256 ?: return base
        val key = "${System.identityHashCode(base)}|$pin|${e.host}"
        return pinnedClients.getOrPut(key) { PinnedTls.client(base, pin, e.host) }
    }

    /**
     * Tries each QR path until one answers; returns the pairing result and the path that worked.
     * A path that answers with an error (wrong/expired code) stops the loop — trying the next path
     * with the same single-use code would only count as another failed attempt on the PC.
     */
    suspend fun pair(offer: PairingOffer, deviceName: String, deviceModel: String): Pair<PairResponse, Endpoint> =
        withContext(Dispatchers.IO) {
            val body = JSONObject()
                .put("code", offer.code)
                .put("deviceName", deviceName)
                .put("deviceModel", deviceModel)
                .toString()
                .toRequestBody(JSON)
            val failures = mutableListOf<String>()
            for (e in RouteSelector.order(offer.endpoints, networks(), null)) {
                val res: Response = try {
                    clientFor(e, quick).newCall(Request.Builder().url(e.url("/api/v1/pair")).post(body).build()).execute()
                } catch (ex: IOException) {
                    failures += "${e.label}: ${describe(ex)}"
                    continue
                }
                res.use {
                    val text = it.body?.string()
                    if (it.isSuccessful) return@withContext PairResponse.parse(text!!) to e
                    throw PairException(errorCode(text), it.code)
                }
            }
            throw IOException(failures.joinToString("\n").ifBlank { "no endpoints" })
        }

    /**
     * Registers this phone's UnifiedPush endpoint and keys with [pc] (`POST /api/v1/push`), trying each
     * path in order. Throws [UnauthorizedException] when the PC no longer knows this phone and
     * [IOException] when no path answered or the PC is too old for push (404).
     */
    suspend fun registerPush(pc: PcEntry, subscription: PushSubscription): PushSubscriptionResponse =
        pushCall(pc, Request.Builder().post(subscription.toJson().toString().toRequestBody(JSON)))

    /** Removes this phone's subscription from [pc] (`DELETE /api/v1/push`). */
    suspend fun unregisterPush(pc: PcEntry): PushSubscriptionResponse = pushCall(pc, Request.Builder().delete())

    private suspend fun pushCall(pc: PcEntry, builder: Request.Builder): PushSubscriptionResponse = withContext(Dispatchers.IO) {
        var last: Exception = IOException("no endpoints")
        for (e in pc.orderedEndpoints(networks())) {
            val req = builder.url(e.url("/api/v1/push")).header("Authorization", "Bearer ${pc.token}").build()
            val res = try {
                clientFor(e, quick).newCall(req).execute()
            } catch (ex: IOException) {
                last = IOException("${e.label}: ${describe(ex)}")
                continue
            }
            res.use {
                val text = it.body?.string()
                when {
                    it.isSuccessful -> return@withContext PushSubscriptionResponse.parse(text!!)
                    it.code == 401 -> throw UnauthorizedException()
                    else -> throw IOException("push registration failed (HTTP ${it.code})")
                }
            }
        }
        throw last
    }

    /** The PC's current endpoints (null on any failure). */
    private fun fetchEndpoints(pc: PcEntry, e: Endpoint): List<Endpoint>? = try {
        val req = Request.Builder().url(e.url("/api/v1/endpoints")).header("Authorization", "Bearer ${pc.token}").build()
        clientFor(e, quick).newCall(req).execute().use { res ->
            if (res.isSuccessful) parseEndpointsResponse(res.body!!.string()) else null
        }
    } catch (_: Exception) {
        null
    }

    /**
     * Live snapshots while collected. Reconnects with backoff on network loss (re-choosing the path
     * for the network the phone is on now); stops on 401 / revoke or a schema mismatch. Cancelling the
     * collector closes the socket. After the first frame on a path the PC's current endpoint list is
     * fetched and handed to [onEndpoints], so DHCP changes or a newly enabled Tailscale follow along.
     * Each attempt starts from [latest] (the stored entry), so path changes made meanwhile are used.
     */
    fun live(
        pc: PcEntry,
        onGoodEndpoint: (Endpoint) -> Unit,
        onEndpoints: (List<Endpoint>) -> Unit = {},
        latest: () -> PcEntry? = { null },
    ): Flow<LiveState> = flow {
        var attempt = 0
        var current = pc
        while (true) {
            latest()?.let { current = it }
            emit(LiveState.Connecting)
            var failure: Throwable? = null
            for (e in current.orderedEndpoints(networks())) {
                try {
                    var first = true
                    socketFlow(e, current).collect { (snapshot, json) ->
                        if (first) {
                            first = false
                            attempt = 0
                            onGoodEndpoint(e)
                            current = current.copy(lastGood = e.key)
                            fetchEndpoints(current, e)?.takeIf { it.isNotEmpty() && it != current.endpoints }?.let {
                                current = current.copy(endpoints = it)
                                onEndpoints(it)
                            }
                        }
                        emit(LiveState.Live(snapshot, json, e))
                    }
                    failure = IOException("closed")
                } catch (ex: UnauthorizedException) {
                    emit(LiveState.Unauthorized)
                    return@flow
                } catch (ex: ProtocolException) {
                    emit(LiveState.Incompatible(ex.message ?: "incompatible"))
                    return@flow
                } catch (ex: IOException) {
                    failure = IOException("${e.label}: ${describe(ex)}")
                    continue
                }
                break // got frames from this path, then lost it → back off and re-pick
            }
            val wait = BACKOFF_SECONDS[minOf(attempt++, BACKOFF_SECONDS.lastIndex)]
            emit(LiveState.Retrying(failure?.message ?: "failed", wait))
            delay(wait * 1000L)
        }
    }.flowOn(Dispatchers.IO)

    private fun socketFlow(e: Endpoint, pc: PcEntry): Flow<Pair<UsageSnapshot, String>> = callbackFlow {
        val request = Request.Builder()
            .url(e.url("/api/v1/live"))
            .header("Authorization", "Bearer ${pc.token}")
            .build()
        val ws = clientFor(e, client).newWebSocket(request, object : WebSocketListener() {
            override fun onMessage(webSocket: WebSocket, text: String) {
                try {
                    trySend(UsageSnapshot.parse(text) to text)
                } catch (ex: ProtocolException) {
                    close(ex)
                } catch (ex: Exception) {
                    close(IOException("bad frame: ${ex.message}"))
                }
            }

            override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) {
                close(if (response?.code == 401) UnauthorizedException() else (t as? IOException ?: IOException(t.message, t)))
            }

            // The PC closes with 1008 when this phone was revoked in the tray mid-session.
            override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                if (code == CLOSE_REVOKED) close(UnauthorizedException()) else close()
                webSocket.close(1000, null)
            }

            override fun onClosed(webSocket: WebSocket, code: Int, reason: String) {
                if (code == CLOSE_REVOKED) close(UnauthorizedException()) else close()
            }
        })
        awaitClose { ws.close(1000, "bye") }
    }

    companion object {
        private val JSON = "application/json; charset=utf-8".toMediaType()
        private val BACKOFF_SECONDS = intArrayOf(1, 2, 5, 10, 30)

        /** WebSocket policy-violation close the PC sends to a revoked device. */
        private const val CLOSE_REVOKED = 1008

        val defaultClient: OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(5, TimeUnit.SECONDS)
            // The PC sends a frame at least every 60 s (heartbeat) and pings every 15 s; 90 s of silence = dead.
            .readTimeout(90, TimeUnit.SECONDS)
            .pingInterval(15, TimeUnit.SECONDS)
            .build()

        /** Short reason for a failed path; a certificate pin mismatch is called out as such. */
        fun describe(e: Throwable): String {
            var c: Throwable? = e
            while (c != null) {
                if (c is PinMismatchException) return c.message ?: "certificate mismatch"
                c = c.cause
            }
            return if (e is SSLException) "TLS: ${e.message}" else e.message ?: e.javaClass.simpleName
        }
    }
}
