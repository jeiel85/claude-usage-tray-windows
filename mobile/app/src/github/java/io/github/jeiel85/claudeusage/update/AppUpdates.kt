package io.github.jeiel85.claudeusage.update

import android.content.Context
import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

/**
 * What the shared app code knows about updates (#257). This `github` build updates itself from GitHub Releases
 * (#228); the `play` build has an `AppUpdates` with the same members that does nothing, because Play delivers its
 * updates. Main code calls only this object, so neither the updater nor its permission reaches the Play build.
 */
internal object AppUpdates {
    /** Whether this build checks for and installs its own updates (shows the Settings → Updates section). */
    const val IN_APP = true

    /** Activity start: opened from the "new version" notification → open the dialog; keep the daily check scheduled. */
    fun onCreate(context: Context, intent: Intent?, firstCreate: Boolean) {
        // Not on recreation (rotation) — the request was already consumed then.
        if (firstCreate && intent.opensUpdate()) UpdateSurface.requestOpen()
        UpdateCheckScheduler.sync(context)
    }

    /** A new intent for the running activity. True when it opens the update dialog, which lives on the home screen. */
    fun onNewIntent(intent: Intent): Boolean {
        if (!intent.opensUpdate()) return false
        UpdateSurface.requestOpen()
        return true
    }

    @Composable
    fun Banner(modifier: Modifier = Modifier) = UpdateSurface.Banner(modifier)

    @Composable
    fun SettingsCard(canNotify: Boolean, onAllowNotifications: () -> Unit) =
        UpdateSurface.SettingsCard(canNotify, onAllowNotifications)

    private fun Intent?.opensUpdate(): Boolean = this?.getBooleanExtra(UpdateNotifier.EXTRA_OPEN_UPDATE, false) == true
}
