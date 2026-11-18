package io.github.jeiel85.claudeusage.update

import android.content.Context
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import io.github.jeiel85.claudeusage.BuildConfig
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.ui.AppSwitch
import io.github.jeiel85.claudeusage.ui.CardShape
import io.github.jeiel85.claudeusage.ui.Palette
import kotlinx.coroutines.launch

/**
 * The Play build's update UI (#284): a one-line home banner and the Settings → Updates card. Both read
 * [StoreUpdates.available] and both lead to the Play listing; nothing here installs anything.
 */
internal object StoreUpdateSurface {

    /**
     * Input : none
     * Output: a banner while Play offers a newer version (tap → Play listing), otherwise nothing
     * 핵심 로직: asks Play when the app comes to the foreground, at most every few hours ([StoreUpdates.checkIfStale]).
     *           Never a modal — updating is not what the user opened the app for.
     */
    @Composable
    fun Banner(modifier: Modifier = Modifier) {
        val context = LocalContext.current
        val lifecycle = LocalLifecycleOwner.current.lifecycle
        LaunchedEffect(Unit) {
            lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) { refresh(context) }
        }
        val versionCode = StoreUpdates.available.collectAsState().value ?: return
        Text(
            text = stringResource(R.string.update_banner, StoreUpdateDecision.versionName(versionCode)),
            color = Palette.Text,
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium,
            modifier = modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(12.dp))
                .background(Palette.Accent.copy(alpha = 0.25f))
                .clickable { StoreUpdates.openStore(context) }
                .padding(horizontal = 16.dp, vertical = 12.dp),
        )
    }

    /** A check that found no newer version also clears a notification left from before (updated through Play since). */
    private suspend fun refresh(context: Context) {
        StoreUpdates.checkIfStale(context)
        if (StoreUpdates.available.value == null) StoreUpdateNotifier.cancel(context)
    }

    /**
     * Input : whether notifications may be posted, how to ask for that permission
     * Output: the "App updates" card — installed version, notification switch with what it does, the newer version
     *         (if any) with a button to the Play listing, "Check now" and its result
     */
    @Composable
    fun SettingsCard(canNotify: Boolean, onAllowNotifications: () -> Unit) {
        val context = LocalContext.current
        val scope = rememberCoroutineScope()
        val preferences = remember(context) { StoreUpdatePreferences(context) }
        var notify by remember { mutableStateOf(preferences.isNotifyEnabled()) }
        var checking by remember { mutableStateOf(false) }
        var result by remember { mutableStateOf<String?>(null) }
        val available by StoreUpdates.available.collectAsState()

        // Opened straight into Settings (without the home screen): still show what Play offers.
        LaunchedEffect(Unit) { refresh(context) }

        Column(
            Modifier.fillMaxWidth().clip(CardShape).background(Palette.Card).border(1.dp, Palette.CardBorder, CardShape).padding(16.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(stringResource(R.string.update_settings_title), color = Palette.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium)
                    Text(stringResource(R.string.update_current_version, BuildConfig.VERSION_NAME), color = Palette.TextDim, fontSize = 11.sp)
                }
                AppSwitch(
                    checked = notify,
                    onCheckedChange = { checked ->
                        preferences.setNotifyEnabled(checked)
                        notify = checked
                        StoreUpdateScheduler.sync(context)
                    },
                )
            }
            Text(stringResource(R.string.update_settings_description), color = Palette.TextSoft, fontSize = 12.sp, modifier = Modifier.padding(top = 6.dp))

            available?.let { versionCode ->
                Row(
                    Modifier
                        .padding(top = 12.dp)
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(12.dp))
                        .background(Palette.Accent.copy(alpha = 0.18f))
                        .padding(start = 14.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        stringResource(R.string.update_result_available, StoreUpdateDecision.versionName(versionCode)),
                        color = Palette.Text,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        modifier = Modifier.weight(1f),
                    )
                    Button(onClick = { StoreUpdates.openStore(context) }) { Text(stringResource(R.string.update_open_store)) }
                }
            }

            Row(verticalAlignment = Alignment.CenterVertically) {
                TextButton(
                    enabled = !checking,
                    onClick = {
                        checking = true
                        result = null
                        scope.launch {
                            result = message(context, StoreUpdates.check(context))
                            checking = false
                        }
                    },
                ) { Text(stringResource(R.string.update_check_now), color = Palette.AccentLight) }
                if (checking) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp, color = Palette.AccentLight)
            }
            result?.let { Text(it, color = Palette.TextSoft, fontSize = 12.sp) }
            if (notify && !canNotify) {
                TextButton(onClick = onAllowNotifications) {
                    Text(stringResource(R.string.update_notifications_off), color = Palette.Warn, fontSize = 12.sp)
                }
            }
        }
    }

    /** The line under "Check now". A newer version needs none — the row with the Play button says it. */
    private fun message(context: Context, result: StoreUpdates.Result): String? = when (result) {
        is StoreUpdates.Result.Available -> null
        StoreUpdates.Result.UpToDate -> context.getString(R.string.update_result_latest, BuildConfig.VERSION_NAME)
        StoreUpdates.Result.NotFromPlay -> context.getString(R.string.update_result_not_from_play)
        StoreUpdates.Result.Failed -> context.getString(R.string.update_result_failed)
    }
}
