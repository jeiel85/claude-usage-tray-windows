package io.github.jeiel85.claudeusage.push

import android.app.BroadcastOptions
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import java.util.UUID

/**
 * The UnifiedPush connector side (spec AND_3.1.0, https://unifiedpush.org/developers/spec/android/),
 * implemented with plain broadcast intents instead of the connector library (Homebase DECISIONS M-3, M-19; #222).
 * A distributor app (ntfy, …) holds the one persistent connection; this app only registers, receives
 * [PushReceiver] broadcasts and acknowledges them.
 */
object UnifiedPush {
    // App → distributor
    const val ACTION_REGISTER = "org.unifiedpush.android.distributor.REGISTER"
    const val ACTION_UNREGISTER = "org.unifiedpush.android.distributor.UNREGISTER"
    const val ACTION_MESSAGE_ACK = "org.unifiedpush.android.distributor.MESSAGE_ACK"

    // Distributor → app
    const val ACTION_NEW_ENDPOINT = "org.unifiedpush.android.connector.NEW_ENDPOINT"
    const val ACTION_MESSAGE = "org.unifiedpush.android.connector.MESSAGE"
    const val ACTION_REGISTRATION_FAILED = "org.unifiedpush.android.connector.REGISTRATION_FAILED"
    const val ACTION_UNREGISTERED = "org.unifiedpush.android.connector.UNREGISTERED"
    const val ACTION_TEMP_UNAVAILABLE = "org.unifiedpush.android.connector.TEMP_UNAVAILABLE"

    const val EXTRA_TOKEN = "token"
    const val EXTRA_ENDPOINT = "endpoint"
    const val EXTRA_BYTES_MESSAGE = "bytesMessage"
    const val EXTRA_ID = "id"
    const val EXTRA_REASON = "reason"
    const val EXTRA_MESSAGE = "message"
    const val EXTRA_PI = "pi"

    /** Legacy (AND_2) extra some distributors still read to know which app registered. */
    private const val EXTRA_APPLICATION = "application"

    /** The spec's identity carrier: an immutable PendingIntent whose creator package is ours. */
    private const val DUMMY_APP = "org.unifiedpush.dummy_app"

    /** Distributors the user can install; ntfy is the common one and works with ntfy.sh, no account. */
    const val NTFY_PACKAGE = "io.heckel.ntfy"

    /** Installed distributor packages (needs the `<queries>` entry for ACTION_REGISTER, API 30+). */
    fun distributors(context: Context): List<String> {
        val pm = context.packageManager
        val intent = Intent(ACTION_REGISTER)
        val receivers = if (Build.VERSION.SDK_INT >= 33) {
            pm.queryBroadcastReceivers(intent, PackageManager.ResolveInfoFlags.of(0))
        } else {
            @Suppress("DEPRECATION")
            pm.queryBroadcastReceivers(intent, 0)
        }
        return receivers.mapNotNull { it.activityInfo?.packageName }.distinct().filter { it != context.packageName }
    }

    fun label(context: Context, pkg: String): String = runCatching {
        val pm = context.packageManager
        val info = if (Build.VERSION.SDK_INT >= 33) {
            pm.getApplicationInfo(pkg, PackageManager.ApplicationInfoFlags.of(0))
        } else {
            @Suppress("DEPRECATION")
            pm.getApplicationInfo(pkg, 0)
        }
        pm.getApplicationLabel(info).toString()
    }.getOrDefault(pkg)

    /**
     * Asks [distributor] for an endpoint. A fresh high-entropy token every time (required after
     * REGISTRATION_FAILED, harmless otherwise); the answer arrives as NEW_ENDPOINT.
     */
    fun register(context: Context, distributor: String) {
        val state = PushState(context)
        val token = UUID.randomUUID().toString()
        state.startRegistration(token, distributor)
        val intent = Intent(ACTION_REGISTER)
            .setPackage(distributor)
            .putExtra(EXTRA_TOKEN, token)
            .putExtra(EXTRA_MESSAGE, context.getString(io.github.jeiel85.claudeusage.R.string.push_distributor_label))
            .putExtra(EXTRA_APPLICATION, context.packageName)
            .putExtra(EXTRA_PI, identity(context))
        send(context, intent)
    }

    /** Tells the distributor to drop our endpoint; the token is forgotten at once (spec MUST). */
    fun unregister(context: Context) {
        val state = PushState(context)
        val token = state.token
        val distributor = state.distributor
        state.clear()
        if (token == null || distributor == null) return
        val intent = Intent(ACTION_UNREGISTER)
            .setPackage(distributor)
            .putExtra(EXTRA_TOKEN, token)
            .putExtra(EXTRA_APPLICATION, context.packageName)
            .putExtra(EXTRA_PI, identity(context))
        send(context, intent)
    }

    fun ack(context: Context, distributor: String, token: String, id: String) {
        send(
            context,
            Intent(ACTION_MESSAGE_ACK).setPackage(distributor).putExtra(EXTRA_TOKEN, token).putExtra(EXTRA_ID, id),
        )
    }

    private fun identity(context: Context): PendingIntent =
        PendingIntent.getBroadcast(context, 0, Intent().setPackage(DUMMY_APP), PendingIntent.FLAG_IMMUTABLE)

    /** On API 34+ the spec identifies the sender by a broadcast that shares its identity. */
    private fun send(context: Context, intent: Intent) {
        if (Build.VERSION.SDK_INT >= 34) {
            val options = BroadcastOptions.makeBasic().setShareIdentityEnabled(true)
            context.sendBroadcast(intent, null, options.toBundle())
        } else {
            context.sendBroadcast(intent)
        }
    }
}
