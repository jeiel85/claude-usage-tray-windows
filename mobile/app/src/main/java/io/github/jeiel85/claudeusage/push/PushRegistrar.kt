package io.github.jeiel85.claudeusage.push

import android.content.Context
import android.util.Log
import io.github.jeiel85.claudeusage.data.PcEntry
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.net.UnauthorizedException
import io.github.jeiel85.claudeusage.net.UsageApi
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.async
import kotlinx.coroutines.awaitAll
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import java.util.concurrent.ConcurrentHashMap

/**
 * Keeps every paired PC told about this phone's push endpoint (`POST /api/v1/push`) — ported from
 * Homebase. Called when the endpoint changes, after pairing, and whenever a PC answers a live
 * connection, so a PC that was offline catches up on the next contact. This is what makes pairing by
 * QR also set up notifications: no topic name to type anywhere (#222).
 */
object PushRegistrar {
    private const val TAG = "CutPush"

    /** Background work that must outlive a screen (a registration started from a broadcast). */
    val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** pcId → the subscription that PC accepted in this process. */
    private val synced = ConcurrentHashMap<String, String>()
    private val locks = ConcurrentHashMap<String, Mutex>()

    private fun syncKey(pc: PcEntry, s: PushSubscription) =
        "${pc.deviceId}|${s.endpoint}|${s.p256dh}|${s.auth}|${s.mutedCategories.joinToString(",")}"

    fun ensureAsync(context: Context, api: UsageApi, pc: PcEntry) {
        val app = context.applicationContext
        scope.launch { ensure(app, api, pc) }
    }

    /** One registration at a time per PC; the subscription is read inside the lock so the latest choice wins. */
    suspend fun ensure(context: Context, api: UsageApi, pc: PcEntry) {
        val state = PushState(context)
        locks.getOrPut(pc.pcId) { Mutex() }.withLock {
            val subscription = state.subscription() ?: return
            val key = syncKey(pc, subscription)
            if (synced[pc.pcId] == key) return
            try {
                api.registerPush(pc, subscription)
                synced[pc.pcId] = key
                state.markRegistered(pc.pcId, true)
            } catch (e: UnauthorizedException) {
                state.markRegistered(pc.pcId, false)
            } catch (e: Exception) {
                Log.i(TAG, "push registration with ${pc.name} deferred: ${e.message}")
            }
        }
    }

    suspend fun ensureAll(context: Context, api: UsageApi, store: PcStore) = coroutineScope {
        store.all().map { async { ensure(context, api, it) } }.awaitAll()
    }

    fun forgetSynced() = synced.clear()

    fun forget(pcId: String) {
        synced.remove(pcId)
    }

    /** Push turned off: remove the subscription from every reachable PC (best effort). */
    suspend fun unregisterAll(context: Context, api: UsageApi, store: PcStore) = coroutineScope {
        forgetSynced()
        val state = PushState(context)
        store.all().map { pc ->
            async {
                runCatching { api.unregisterPush(pc) }
                state.markRegistered(pc.pcId, false)
            }
        }.awaitAll()
    }
}
