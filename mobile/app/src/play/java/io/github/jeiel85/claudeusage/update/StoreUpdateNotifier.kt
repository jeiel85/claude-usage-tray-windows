package io.github.jeiel85.claudeusage.update

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import io.github.jeiel85.claudeusage.MainActivity
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.push.PushNotifier

/**
 * One Android notification per new Play version (#284). Tapping it opens the app, which hands straight over to the
 * Play listing ([EXTRA_OPEN_STORE]) — going through the app keeps a web fallback when there is no Play Store app,
 * which a PendingIntent aimed at the store could not. Its own channel, apart from PC alerts, like the GitHub build's.
 */
internal object StoreUpdateNotifier {
    const val EXTRA_OPEN_STORE = "io.github.jeiel85.claudeusage.OPEN_STORE"

    private const val CHANNEL_ID = "cut.app.update"

    // Fixed ID — a later version replaces the earlier notice instead of stacking.
    private const val NOTIFICATION_ID = 0x0c_0284

    /**
     * Input : context, the versionCode Play offers
     * Output: whether it was actually posted (false without permission or with this channel turned off — then only
     *         the in-app banner remains, and the caller doesn't record the version as notified)
     */
    fun show(context: Context, versionCode: Int): Boolean {
        if (!PushNotifier.canNotify(context)) return false
        ensureChannel(context)
        val channel = context.getSystemService(NotificationManager::class.java).getNotificationChannel(CHANNEL_ID)
        if (channel == null || channel.importance == NotificationManager.IMPORTANCE_NONE) return false
        val open = Intent(context, MainActivity::class.java)
            .putExtra(EXTRA_OPEN_STORE, true)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(
                context.getString(
                    R.string.update_notification_title,
                    context.getString(R.string.app_name),
                    StoreUpdateDecision.versionName(versionCode),
                ),
            )
            .setContentText(context.getString(R.string.update_notification_text))
            .setCategory(NotificationCompat.CATEGORY_RECOMMENDATION)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setOnlyAlertOnce(true)
            .setContentIntent(
                PendingIntent.getActivity(context, NOTIFICATION_ID, open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT),
            )
            .setAutoCancel(true)
            .build()
        return try {
            NotificationManagerCompat.from(context).notify(NOTIFICATION_ID, notification)
            true
        } catch (_: SecurityException) {
            false // permission revoked between the check and the post
        }
    }

    /** No newer version any more (updated, or Play withdrew it): clear a notice that would lead nowhere. */
    fun cancel(context: Context) {
        NotificationManagerCompat.from(context).cancel(NOTIFICATION_ID)
    }

    private fun ensureChannel(context: Context) {
        context.getSystemService(NotificationManager::class.java).createNotificationChannel(
            NotificationChannel(CHANNEL_ID, context.getString(R.string.update_channel_name), NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = context.getString(R.string.update_channel_description)
            },
        )
    }
}
