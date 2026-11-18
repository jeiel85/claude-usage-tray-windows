package io.github.jeiel85.claudeusage.update

import android.content.Context
import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import io.github.jeiel85.claudeusage.BuildConfig

/**
 * The Play build (#257, #284): Google Play installs updates, and Play policy forbids an app updating itself from
 * elsewhere, so this build has no downloader, no installer and no `REQUEST_INSTALL_PACKAGES`. It only asks the Google
 * Play app whether a newer version is out for this user ([StoreUpdates]) and sends the user to the Play listing.
 * Same members as the `github` build's `AppUpdates`, so the shared code needs no build checks.
 */
internal object AppUpdates {
    /** Libraries only this build ships, for Settings → Open-source licenses. */
    val THIRD_PARTY: List<Pair<String, String>> = listOf(
        "Google Play In-App Updates Library (Play Core)" to "Play Core Software Development Kit Terms of Service",
        "Google Play services Basement, Tasks" to "Android Software Development Kit License",
    )

    /** Activity start: opened from the "new version" notification → Play listing; keep the background check scheduled. */
    fun onCreate(context: Context, intent: Intent?, firstCreate: Boolean) {
        if (BuildConfig.DEBUG) intent.debugFakeUpdate()?.let { StoreUpdates.useFakeUpdate(it) }
        // Not on recreation (rotation) — the request was already consumed then.
        if (firstCreate && intent.opensStore()) StoreUpdates.openStore(context)
        StoreUpdateScheduler.sync(context)
    }

    /** A new intent for the running activity. The store opens on top, so the app's own screens stay as they were. */
    fun onNewIntent(context: Context, intent: Intent): Boolean {
        if (intent.opensStore()) StoreUpdates.openStore(context)
        return false
    }

    @Composable
    fun Banner(modifier: Modifier = Modifier) = StoreUpdateSurface.Banner(modifier)

    @Composable
    fun SettingsCard(canNotify: Boolean, onAllowNotifications: () -> Unit) =
        StoreUpdateSurface.SettingsCard(canNotify, onAllowNotifications)

    private fun Intent?.opensStore(): Boolean = this?.getBooleanExtra(StoreUpdateNotifier.EXTRA_OPEN_STORE, false) == true

    /**
     * Debug builds only: `adb shell am start -n com.jeiel85.aiusage/io.github.jeiel85.claudeusage.MainActivity
     * --ei cut.fakeStoreUpdate 16700` pretends Play offers that versionCode, so the banner, card and notification can be
     * checked on a sideloaded build (the real API answers only for a copy installed from Play).
     */
    private fun Intent?.debugFakeUpdate(): Int? =
        this?.getIntExtra(EXTRA_FAKE_STORE_UPDATE, 0)?.takeIf { it > 0 }

    private const val EXTRA_FAKE_STORE_UPDATE = "cut.fakeStoreUpdate"
}
