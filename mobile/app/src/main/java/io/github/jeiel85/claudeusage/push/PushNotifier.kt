package io.github.jeiel85.claudeusage.push

import android.Manifest
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import androidx.compose.ui.graphics.toArgb
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import io.github.jeiel85.claudeusage.MainActivity
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.ui.Palette

/**
 * Shows decrypted pushes on one of two Android channels (#297): [URGENT_CHANNEL] sounds and vibrates for what needs
 * the user now ([NotificationGroups.isUrgent]); [QUIET_CHANNEL] only posts to the shade. Which kinds arrive at all is
 * the app's own "Notifications to receive" switches (the PC skips muted categories), so channels split by urgency,
 * not by kind — the user tunes the sound of each in Android's settings. The subtext names the account the alert is about (#262;
 * the PC name when there is none — see [PushMessage.source]) and the accent is the service's color, so
 * alerts for different accounts and services can be told apart at a glance.
 */
object PushNotifier {

    fun canNotify(context: Context): Boolean {
        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(context, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            return false
        }
        return NotificationManagerCompat.from(context).areNotificationsEnabled()
    }

    fun show(context: Context, message: PushMessage) {
        if (!canNotify(context)) return
        ensureChannels(context)
        val urgent = NotificationGroups.isUrgent(message.priority)
        // Weather alerts carry a link to the forecast page; everything else opens the app.
        val target = message.clickUrl?.let { Intent(Intent.ACTION_VIEW, Uri.parse(it)) }
            ?: Intent(context, MainActivity::class.java)
        val open = PendingIntent.getActivity(
            context,
            message.id.hashCode(),
            target.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, if (urgent) URGENT_CHANNEL else QUIET_CHANNEL)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(message.title)
            .setContentText(message.body)
            .setStyle(message.body?.let { NotificationCompat.BigTextStyle().bigText(it) })
            .setSubText(message.source)
            .setWhen(message.tsMs)
            .setShowWhen(true)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            .setPriority(if (urgent) NotificationCompat.PRIORITY_HIGH else NotificationCompat.PRIORITY_LOW)
            .setContentIntent(open)
            .apply { message.provider?.let { setColor(Palette.forProvider(it).toArgb()) } }
            .setAutoCancel(true)
            .build()
        try {
            NotificationManagerCompat.from(context).notify(message.id.hashCode(), notification)
        } catch (_: SecurityException) {
            // Permission revoked between the check and the post.
        }
    }

    const val URGENT_CHANNEL = "cut.alert.urgent"
    const val QUIET_CHANNEL = "cut.alert.quiet"

    /**
     * Creates the two alert channels and deletes the per-category ones (`cut.usage`, `cut.weather`, …) that versions
     * before #297 made. Those were all high or default importance — so everything vibrated — and an app can't lower
     * a channel's importance once created, so they are replaced, not changed. A kind the user had blocked in
     * Android's settings becomes an in-app mute first (the PC learns it at the next registration), so deleting the
     * channel doesn't bring those alerts back. Safe to call repeatedly.
     */
    fun ensureChannels(context: Context) {
        val nm = context.getSystemService(NotificationManager::class.java)
        val legacy = nm.notificationChannels.filter { isLegacyChannel(it.id) }
        val blocked = blockedCategories(legacy.map { it.id to it.importance })
        if (blocked.isNotEmpty()) PushState(context).apply { mutedCategories = mutedCategories + blocked }
        legacy.forEach { nm.deleteNotificationChannel(it.id) }
        nm.createNotificationChannels(
            listOf(
                NotificationChannel(URGENT_CHANNEL, context.getString(R.string.channel_urgent), NotificationManager.IMPORTANCE_HIGH).apply {
                    description = context.getString(R.string.channel_urgent_desc)
                    enableVibration(true)
                },
                NotificationChannel(QUIET_CHANNEL, context.getString(R.string.channel_quiet), NotificationManager.IMPORTANCE_LOW).apply {
                    description = context.getString(R.string.channel_quiet_desc)
                    enableVibration(false)
                    setSound(null, null)
                },
            ),
        )
    }

    /**
     * A channel from before #297: `cut.<category>`. PC categories are `[a-z0-9_-]` only, so they never contain a dot —
     * unlike the current ids ([URGENT_CHANNEL], [QUIET_CHANNEL], the update channel `cut.app.update`).
     */
    internal fun isLegacyChannel(id: String): Boolean = id.startsWith("cut.") && '.' !in id.removePrefix("cut.")

    /** Kinds whose pre-#297 channel the user had turned off (importance NONE), as app categories the user can mute. */
    internal fun blockedCategories(legacy: List<Pair<String, Int>>): Set<String> = legacy
        .filter { (id, importance) -> isLegacyChannel(id) && importance == NotificationManager.IMPORTANCE_NONE }
        .map { (id, _) -> id.removePrefix("cut.") }
        .filter { category -> NotificationGroups.ALL.any { it.category == category } }
        .toSet()
}
