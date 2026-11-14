package io.github.jeiel85.claudeusage.push

import android.app.Service
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Binder
import android.os.IBinder
import android.util.Log
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.net.NetworkProbe
import io.github.jeiel85.claudeusage.net.UsageApi
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Broadcasts from the distributor (UnifiedPush AND_3), ported from Homebase. Anything whose token
 * isn't our current one is ignored; a MESSAGE must also decrypt with this phone's key and auth secret
 * (AES-GCM tag), so a forged broadcast from another app shows nothing. Messages from a PC that isn't
 * paired are dropped, and the same event sent by several PCs is shown once ([EventDedupe]).
 */
class PushReceiver : BroadcastReceiver() {

    override fun onReceive(context: Context, intent: Intent) {
        val state = PushState(context)
        val token = intent.getStringExtra(UnifiedPush.EXTRA_TOKEN)
        if (token == null || token != state.token) return
        val distributor = state.distributor ?: return
        val id = intent.getStringExtra(UnifiedPush.EXTRA_ID)

        when (intent.action) {
            UnifiedPush.ACTION_NEW_ENDPOINT -> {
                val endpoint = intent.getStringExtra(UnifiedPush.EXTRA_ENDPOINT)
                if (id != null) UnifiedPush.ack(context, distributor, token, id)
                if (endpoint.isNullOrBlank()) return
                state.setEndpoint(endpoint)
                // Also on an unchanged endpoint: a PC that dropped the subscription after a 404/410 gets it back.
                PushRegistrar.forgetSynced()
                registerWithAllPcs(context)
            }

            UnifiedPush.ACTION_MESSAGE -> {
                val bytes = intent.getByteArrayExtra(UnifiedPush.EXTRA_BYTES_MESSAGE)
                if (id != null) UnifiedPush.ack(context, distributor, token, id)
                if (bytes != null) handleMessage(context, state, bytes)
            }

            UnifiedPush.ACTION_REGISTRATION_FAILED -> {
                if (state.endpoint != null) return // spec: ignore once NEW_ENDPOINT arrived for this token
                val reason = intent.getStringExtra(UnifiedPush.EXTRA_REASON) ?: "INTERNAL_ERROR"
                state.lastError = context.getString(R.string.push_err_rejected, reason)
                state.clear()
            }

            UnifiedPush.ACTION_UNREGISTERED -> {
                state.lastError = context.getString(R.string.push_err_unregistered)
                state.clear()
                PushRegistrar.forgetSynced()
            }

            UnifiedPush.ACTION_TEMP_UNAVAILABLE -> {
                state.lastError = context.getString(R.string.push_err_temp)
            }
        }
    }

    private fun handleMessage(context: Context, state: PushState, bytes: ByteArray) {
        val plain = try {
            val keys = state.keys()
            WebPush.decrypt(bytes, keys.privateKey, keys.publicKey, keys.authSecret).toString(Charsets.UTF_8)
        } catch (e: Exception) {
            Log.w(TAG, "dropped an undecryptable push: ${e.javaClass.simpleName}")
            return
        }
        if (PushEndpointsUpdate.kindOf(plain) == PushEndpointsUpdate.KIND) {
            handleEndpoints(context, plain)
            return
        }
        val message = try {
            PushMessage.parse(plain)
        } catch (e: Exception) {
            Log.w(TAG, "dropped an unreadable push: ${e.message}")
            return
        }
        if (PcStore(context).get(message.pcId) == null) {
            Log.i(TAG, "dropped a push from an unpaired PC")
            return
        }
        // The PC skips muted categories itself; this catches the moments before the PC heard about a change.
        if (NotificationGroups.isMuted(message.category, state.mutedCategories)) return
        val show = synchronized(DEDUPE_LOCK) {
            val (fresh, json) = EventDedupe.check(state.recentEvents, message.eventKey, message.eventToleranceSec, System.currentTimeMillis())
            state.recentEvents = json
            fresh
        }
        if (show) {
            // Kept for the notification center (#235) even when Android notifications are off — that is when
            // the history is the only place the alert can be seen.
            NotificationHistory.record(context, message)
            PushNotifier.show(context, message)
        } else {
            Log.i(TAG, "duplicate event ${message.eventKey} from ${message.pcName}")
        }
    }

    /**
     * A PC's new paths (#225): verified with that PC's token, stored silently (no notification). The live
     * hub re-reads the stored entry on every retry, so a PC that was unreachable is tried on its new path next.
     */
    private fun handleEndpoints(context: Context, plain: String) {
        val store = PcStore(context)
        val pc = store.get(PushEndpointsUpdate.pcIdOf(plain)) ?: run {
            Log.i(TAG, "dropped a path update from an unpaired PC")
            return
        }
        val update = try {
            PushEndpointsUpdate.verify(plain, pc.token)
        } catch (e: Exception) {
            Log.w(TAG, "dropped a path update: ${e.message}")
            return
        }
        val applied = store.applyPushedEndpoints(update)
        Log.i(TAG, "path update from ${pc.name}: ${if (applied) "applied" else "older than the last one, ignored"}")
    }

    /** A broadcast may only run ~10 s; PCs not reached by then catch up on their next live connection. */
    private fun registerWithAllPcs(context: Context) {
        val pending = goAsync()
        val app = context.applicationContext
        PushRegistrar.scope.launch {
            try {
                withTimeoutOrNull(8_000) {
                    PushRegistrar.ensureAll(app, UsageApi(networks = NetworkProbe(app)::current), PcStore(app))
                }
            } finally {
                pending.finish()
            }
        }
    }

    private companion object {
        const val TAG = "CutPush"
        val DEDUPE_LOCK = Any()
    }
}

/**
 * Required by AND_3: the distributor may bind this for a few seconds so the app is raised to the
 * foreground importance while it handles a message. Nothing to do here besides existing.
 */
class RaiseToForegroundService : Service() {
    private val binder = Binder()
    override fun onBind(intent: Intent?): IBinder = binder
}
