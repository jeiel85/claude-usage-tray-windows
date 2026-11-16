package io.github.jeiel85.claudeusage.update

import android.content.Context
import android.content.Intent
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier

/**
 * The Play build (#257): Google Play installs updates, and Play policy forbids an app updating itself from elsewhere,
 * so this build has no updater, no update UI and no `REQUEST_INSTALL_PACKAGES`. Same members as the `github` build's
 * `AppUpdates`, so the shared code needs no build checks.
 */
internal object AppUpdates {
    const val IN_APP = false

    @Suppress("UNUSED_PARAMETER")
    fun onCreate(context: Context, intent: Intent?, firstCreate: Boolean) = Unit

    @Suppress("UNUSED_PARAMETER")
    fun onNewIntent(intent: Intent): Boolean = false

    @Suppress("UNUSED_PARAMETER")
    @Composable
    fun Banner(modifier: Modifier = Modifier) = Unit

    @Suppress("UNUSED_PARAMETER")
    @Composable
    fun SettingsCard(canNotify: Boolean, onAllowNotifications: () -> Unit) = Unit
}
