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
 * 새 버전을 Android 알림으로 한 번 알린다(Homebase 이식). 누르면 앱이 열리고 업데이트 창이 바로 뜬다
 * ([EXTRA_OPEN_UPDATE] → [UpdateSurface.requestOpen]). 채널은 PC 알림과 따로 둔다 — 그쪽을 꺼도 앱 업데이트 알림은 살아 있고
 * 그 반대도 마찬가지다.
 */
internal object UpdateNotifier {
    const val EXTRA_OPEN_UPDATE = "io.github.jeiel85.claudeusage.OPEN_UPDATE"

    private const val CHANNEL_ID = "cut.app.update"

    // 고정 ID — 새 버전이 또 나오면 이전 알림을 덮어쓴다(쌓이지 않는다).
    private const val NOTIFICATION_ID = 0x0c_0228

    /**
     * Input : 컨텍스트, 알릴 버전
     * Output: 실제로 게시했는가
     * 핵심 로직: 권한이 없거나 이 채널만 꺼져 있으면 false — 그때는 앱 안 배너만 남는다. 채널을 따로 보는 이유: 꺼진 채널에
     *           게시하면 Android 가 예외 없이 버려서, 성공으로 치면 그 버전을 "알렸다" 고 기억해 채널을 다시 켜도 영영 안 알린다.
     */
    fun show(context: Context, manifest: UpdateManifest): Boolean {
        if (!PushNotifier.canNotify(context)) return false
        ensureChannel(context)
        val channel = context.getSystemService(NotificationManager::class.java).getNotificationChannel(CHANNEL_ID)
        if (channel == null || channel.importance == NotificationManager.IMPORTANCE_NONE) return false
        val open = PendingIntent.getActivity(
            context,
            NOTIFICATION_ID,
            Intent(context, MainActivity::class.java)
                .putExtra(EXTRA_OPEN_UPDATE, true)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val notification = NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(context.getString(R.string.update_notification_title, manifest.versionName))
            .setContentText(context.getString(R.string.update_notification_text))
            .setCategory(NotificationCompat.CATEGORY_RECOMMENDATION)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setOnlyAlertOnce(true)
            .setContentIntent(open)
            .setAutoCancel(true)
            .build()
        return try {
            NotificationManagerCompat.from(context).notify(NOTIFICATION_ID, notification)
            true
        } catch (_: SecurityException) {
            false // 확인과 게시 사이에 권한이 회수됐다.
        }
    }

    /** 설치했거나 건너뛰었거나 확인을 껐을 때 남은 알림을 치운다. */
    fun cancel(context: Context) {
        NotificationManagerCompat.from(context).cancel(NOTIFICATION_ID)
    }

    private fun ensureChannel(context: Context) {
        val nm = context.getSystemService(NotificationManager::class.java)
        if (nm.getNotificationChannel(CHANNEL_ID) != null) return
        nm.createNotificationChannel(
            NotificationChannel(CHANNEL_ID, context.getString(R.string.update_channel_name), NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = context.getString(R.string.update_channel_description)
            },
        )
    }
}
