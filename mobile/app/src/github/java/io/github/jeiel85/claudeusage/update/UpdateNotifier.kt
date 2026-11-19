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
 * 새 버전을 Android 알림으로 한 번 알린다(Homebase 이식). 자동 설치(#281)의 "확인 필요"·"업데이트됨" 도 같은 자리에 띄운다. 누르면 앱이 열리고 업데이트 창이 바로 뜬다
 * ([EXTRA_OPEN_UPDATE] → [UpdateSurface.requestOpen]). 채널은 PC 알림과 따로 둔다 — 그쪽을 꺼도 앱 업데이트 알림은 살아 있고
 * 그 반대도 마찬가지다. 소리는 사용자가 눌러야 진행되는 "확인 필요"([UpdateChannels.ACTION])만 내고, 새 버전·업데이트됨은
 * 조용한 채널([UpdateChannels.QUIET])로 보낸다(#299).
 */
internal object UpdateNotifier {
    const val EXTRA_OPEN_UPDATE = "io.github.jeiel85.claudeusage.OPEN_UPDATE"

    // 고정 ID — 새 버전이 또 나오면 이전 알림을 덮어쓴다(쌓이지 않는다).
    private const val NOTIFICATION_ID = 0x0c_0228

    /**
     * Input : 컨텍스트, 알릴 버전
     * Output: 실제로 게시했는가
     * 핵심 로직: 누르면 앱이 열리고 업데이트 창이 뜬다. 권한이 없거나 이 채널만 꺼져 있으면 false — 그때는 앱 안 배너만 남는다.
     */
    fun show(context: Context, manifest: UpdateManifest): Boolean {
        val open = Intent(context, MainActivity::class.java)
            .putExtra(EXTRA_OPEN_UPDATE, true)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        return post(
            context,
            UpdateChannels.QUIET,
            title = context.getString(R.string.update_notification_title, manifest.versionName),
            text = context.getString(R.string.update_notification_text),
            open = PendingIntent.getActivity(context, NOTIFICATION_ID, open, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT),
        )
    }

    /**
     * 자동 설치를 시스템이 확인 없이 허락하지 않았을 때(#281). 누르면 시스템의 설치 확인 화면이 바로 뜬다 — 이미 받아 확인한
     * 세션을 그대로 쓰므로 다시 내려받지 않는다.
     */
    fun showConfirm(context: Context, versionName: String, confirm: Intent): Boolean = post(
        context,
        UpdateChannels.ACTION,
        title = context.getString(R.string.update_confirm_title, versionName),
        text = context.getString(R.string.update_confirm_text),
        open = PendingIntent.getActivity(
            context,
            NOTIFICATION_ID,
            confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        ),
    )

    /** 백그라운드에서 새 버전으로 바뀌었다는 한 번의 알림(#281). 앱을 열면 [cancel] 로 사라진다. */
    fun showUpdated(context: Context, versionName: String): Boolean = post(
        context,
        UpdateChannels.QUIET,
        title = context.getString(R.string.update_installed_title, versionName),
        text = context.getString(R.string.update_installed_text),
        open = PendingIntent.getActivity(
            context,
            NOTIFICATION_ID,
            Intent(context, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        ),
    )

    /**
     * 채널을 따로 보는 이유: 꺼진 채널에 게시하면 Android 가 예외 없이 버려서, 성공으로 치면 그 버전을 "알렸다" 고 기억해 채널을
     * 다시 켜도 영영 안 알린다.
     */
    private fun post(context: Context, channelId: String, title: String, text: String, open: PendingIntent): Boolean {
        if (!PushNotifier.canNotify(context)) return false
        ensureChannels(context)
        val channel = context.getSystemService(NotificationManager::class.java).getNotificationChannel(channelId)
        if (channel == null || channel.importance == NotificationManager.IMPORTANCE_NONE) return false
        val notification = NotificationCompat.Builder(context, channelId)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(title)
            .setContentText(text)
            .setCategory(NotificationCompat.CATEGORY_RECOMMENDATION)
            // Android 8 미만용 — 8 이상은 채널 중요도를 따른다.
            .setPriority(if (channelId == UpdateChannels.ACTION) NotificationCompat.PRIORITY_DEFAULT else NotificationCompat.PRIORITY_LOW)
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

    /**
     * 앱 시작([AppUpdates.ensureChannels])과 게시 직전에 부른다. 이미 있으면 이름·설명만 갱신된다(중요도 등 사용자가 바꾼 값은
     * 그대로) — 문구가 바뀐 기존 설치본에도 반영된다. 옛 채널은 [UpdateChannels.ensure] 가 지운다.
     */
    fun ensureChannels(context: Context) {
        UpdateChannels.ensure(
            context.getSystemService(NotificationManager::class.java),
            listOf(
                NotificationChannel(UpdateChannels.QUIET, context.getString(R.string.update_channel_name), NotificationManager.IMPORTANCE_LOW).apply {
                    description = context.getString(R.string.update_channel_description)
                    setSound(null, null)
                    enableVibration(false)
                },
                NotificationChannel(UpdateChannels.ACTION, context.getString(R.string.update_action_channel_name), NotificationManager.IMPORTANCE_DEFAULT).apply {
                    description = context.getString(R.string.update_action_channel_description)
                },
            ),
        )
    }
}
