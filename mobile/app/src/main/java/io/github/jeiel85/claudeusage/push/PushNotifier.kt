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
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.core.content.ContextCompat
import io.github.jeiel85.claudeusage.MainActivity
import io.github.jeiel85.claudeusage.R

/**
 * Shows decrypted pushes. One Android channel per category (usage, rate limit, weather, …) so the user
 * can silence one kind from Android's settings; the PC name goes in the subtext. Alerts about one
 * account share a group so several of them stack instead of filling the shade.
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
        val channelId = ensureChannel(context, message.category)
        // Weather alerts carry a link to the forecast page; everything else opens the app.
        val target = message.clickUrl?.let { Intent(Intent.ACTION_VIEW, Uri.parse(it)) }
            ?: Intent(context, MainActivity::class.java)
        val open = PendingIntent.getActivity(
            context,
            message.id.hashCode(),
            target.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val high = message.priority == "warning" || message.priority == "critical"
        val notification = NotificationCompat.Builder(context, channelId)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(message.title)
            .setContentText(message.body)
            .setStyle(message.body?.let { NotificationCompat.BigTextStyle().bigText(it) })
            .setSubText(message.pcName)
            .setWhen(message.tsMs)
            .setShowWhen(true)
            .setCategory(NotificationCompat.CATEGORY_STATUS)
            .setPriority(if (high) NotificationCompat.PRIORITY_HIGH else NotificationCompat.PRIORITY_DEFAULT)
            .setContentIntent(open)
            .setAutoCancel(true)
            .build()
        try {
            NotificationManagerCompat.from(context).notify(message.id.hashCode(), notification)
        } catch (_: SecurityException) {
            // Permission revoked between the check and the post.
        }
    }

    private fun ensureChannel(context: Context, category: String): String {
        val id = "cut.$category"
        val nm = context.getSystemService(NotificationManager::class.java)
        if (nm.getNotificationChannel(id) == null) {
            val importance = if (category == "weather" || category == "reset") {
                NotificationManager.IMPORTANCE_DEFAULT
            } else {
                NotificationManager.IMPORTANCE_HIGH
            }
            nm.createNotificationChannel(NotificationChannel(id, context.getString(NotificationGroups.label(category)), importance))
        }
        return id
    }
}
