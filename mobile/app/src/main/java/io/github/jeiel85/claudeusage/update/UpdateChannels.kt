package io.github.jeiel85.claudeusage.update

import android.app.NotificationChannel
import android.app.NotificationManager

/**
 * App-update notification channels, shared by both builds (#299).
 *
 * The old single channel [LEGACY] was IMPORTANCE_DEFAULT, so "a new version is out" and "updated in the background"
 * made a sound like an alert. Now only [ACTION] (the GitHub build's "confirm the install", which waits for the user)
 * keeps the default importance; everything else goes to the silent [QUIET]. A channel's importance can't be changed by
 * the app once created, so these are new ids and the old channel is deleted. A user who had switched the old channel
 * off gets the new ones created off too, so deleting it doesn't bring update notices back.
 */
internal object UpdateChannels {
    const val LEGACY = "cut.app.update"
    const val QUIET = "cut.app.update.quiet"
    const val ACTION = "cut.app.update.action"

    /**
     * Input : the notification manager, the channels to create (with their intended importance)
     * Output: none — deletes [LEGACY] and creates [channels]. Existing channels keep the user's settings (Android only
     *         updates name and description), so this is safe to call on every start and before every post.
     */
    fun ensure(nm: NotificationManager, channels: List<NotificationChannel>) {
        val legacyImportance = nm.getNotificationChannel(LEGACY)?.importance
        if (legacyImportance != null) nm.deleteNotificationChannel(LEGACY)
        channels.forEach { it.importance = importanceFor(it.importance, legacyImportance) }
        nm.createNotificationChannels(channels)
    }

    /** The importance to create a new channel with: off if the user had turned the old channel off, else [intended]. */
    internal fun importanceFor(intended: Int, legacyImportance: Int?): Int =
        if (legacyImportance == NotificationManager.IMPORTANCE_NONE) NotificationManager.IMPORTANCE_NONE else intended
}
