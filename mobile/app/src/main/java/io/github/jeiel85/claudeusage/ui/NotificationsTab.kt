package io.github.jeiel85.claudeusage.ui

import android.Manifest
import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.os.PowerManager
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
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
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.update.UpdateSurface
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.net.UsageApi
import io.github.jeiel85.claudeusage.push.NotificationGroups
import io.github.jeiel85.claudeusage.push.PushNotifier
import io.github.jeiel85.claudeusage.push.PushRegistrar
import io.github.jeiel85.claudeusage.push.PushState
import io.github.jeiel85.claudeusage.push.UnifiedPush
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/** What the push card shows, re-read while visible (the receiver writes the state). */
private data class PushView(
    val distributors: List<String>,
    val distributor: String?,
    val registering: Boolean,
    val endpoint: String?,
    val userDisabled: Boolean,
    val registeredPcs: Int,
    val canNotify: Boolean,
    val error: String?,
    val muted: Set<String>,
    /** The distributor keeps its own connection; without a battery exemption Doze delays it. */
    val distributorOptimized: Boolean,
)

private fun readView(context: Context, store: PcStore): PushView {
    val state = PushState(context)
    val pcIds = store.all().map { it.pcId }.toSet()
    return PushView(
        distributors = UnifiedPush.distributors(context),
        distributor = state.distributor,
        registering = state.token != null && state.endpoint == null,
        endpoint = state.endpoint,
        userDisabled = state.userDisabled,
        registeredPcs = state.registeredPcs.count { it in pcIds },
        canNotify = PushNotifier.canNotify(context),
        error = state.lastError,
        muted = state.mutedCategories,
        distributorOptimized = state.distributor?.let {
            !context.getSystemService(PowerManager::class.java).isIgnoringBatteryOptimizations(it)
        } ?: false,
    )
}

private fun enablePush(context: Context, distributor: String) {
    val state = PushState(context)
    state.userDisabled = false
    state.lastError = null
    PushRegistrar.forgetSynced()
    UnifiedPush.register(context, distributor)
}

/** Asked once per process, so declining doesn't bring the dialog back every two seconds. */
private object NotificationAsk {
    var asked = false
}

/**
 * The "Notifications" tab (#222). Pairing a PC by QR is all it takes: with exactly one distributor
 * installed ([PushAutoSetup]) push turns itself on and every paired PC is told where to send. With none
 * installed it explains how to get ntfy — the same app the old topic-based alerts used, so most users
 * already have it.
 */
@Composable
fun NotificationsTab(store: PcStore, api: UsageApi) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var view by remember { mutableStateOf(readView(context, store)) }
    var picking by remember { mutableStateOf(false) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        view = readView(context, store)
    }
    val pcCount = store.all().size

    fun enable(distributor: String) {
        enablePush(context, distributor)
        if (Build.VERSION.SDK_INT >= 33 && !PushNotifier.canNotify(context)) {
            NotificationAsk.asked = true
            permission.launch(Manifest.permission.POST_NOTIFICATIONS)
        }
        view = readView(context, store)
    }

    LaunchedEffect(Unit) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                view = readView(context, store)
                delay(2_000)
            }
        }
    }

    val on = view.distributor != null
    Column(verticalArrangement = Arrangement.spacedBy(12.dp)) {
        Card {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                SwitchRow(
                    title = stringResource(R.string.push_title),
                    checked = on,
                    enabled = view.distributors.isNotEmpty(),
                    onChange = { checked ->
                        if (checked) {
                            when (view.distributors.size) {
                                0 -> Unit
                                1 -> enable(view.distributors.single())
                                else -> picking = true
                            }
                        } else {
                            PushState(context).userDisabled = true
                            UnifiedPush.unregister(context)
                            scope.launch { PushRegistrar.unregisterAll(context.applicationContext, api, store) }
                            view = readView(context, store)
                        }
                    },
                )
                val description = when {
                    view.distributors.isEmpty() -> stringResource(R.string.push_need_ntfy)
                    !on -> stringResource(R.string.push_off)
                    view.registering -> stringResource(R.string.push_registering, UnifiedPush.label(context, view.distributor!!))
                    else -> stringResource(R.string.push_on, UnifiedPush.label(context, view.distributor!!), view.registeredPcs, pcCount)
                }
                Text(description, color = Palette.TextSoft, fontSize = 12.sp)
                if (view.distributors.isEmpty()) {
                    TextButton(onClick = {
                        openUri(context, "market://details?id=${UnifiedPush.NTFY_PACKAGE}",
                            "https://play.google.com/store/apps/details?id=${UnifiedPush.NTFY_PACKAGE}")
                    }) { Text(stringResource(R.string.push_get_ntfy), color = Palette.AccentLight) }
                }
                if (on && !view.canNotify) {
                    Text(stringResource(R.string.push_permission_needed), color = Palette.Warn, fontSize = 12.sp)
                    TextButton(onClick = {
                        if (Build.VERSION.SDK_INT >= 33) permission.launch(Manifest.permission.POST_NOTIFICATIONS)
                        else openNotificationSettings(context)
                    }) { Text(stringResource(R.string.push_allow), color = Palette.AccentLight) }
                }
                if (on && view.distributorOptimized) {
                    Text(stringResource(R.string.push_battery_hint), color = Palette.TextDim, fontSize = 11.sp)
                }
                view.error?.let { Text(it, color = Palette.Warn, fontSize = 11.sp) }
            }
        }

        Text(stringResource(R.string.push_categories), color = Palette.TextSoft, fontSize = 12.sp, fontWeight = FontWeight.SemiBold,
            modifier = Modifier.padding(start = 4.dp, top = 4.dp))
        Card {
            Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
                NotificationGroups.ALL.forEach { group ->
                    SwitchRow(
                        title = stringResource(group.label),
                        subtitle = stringResource(group.description),
                        checked = group.category !in view.muted,
                        enabled = true,
                        onChange = { checked ->
                            val state = PushState(context)
                            state.mutedCategories = if (checked) state.mutedCategories - group.category else state.mutedCategories + group.category
                            view = readView(context, store)
                            // Re-register everywhere so each PC skips the category before sending (saves the ntfy daily quota).
                            scope.launch { PushRegistrar.ensureAll(context.applicationContext, api, store) }
                        },
                    )
                }
            }
        }
        Text(stringResource(R.string.push_privacy), color = Palette.TextDim, fontSize = 11.sp, modifier = Modifier.padding(horizontal = 4.dp))

        // App updates (#228) live here because this tab is the app's settings: what the phone receives, and from where.
        UpdateSurface.SettingsCard(
            canNotify = view.canNotify,
            onAllowNotifications = {
                if (Build.VERSION.SDK_INT >= 33 && !NotificationAsk.asked) {
                    NotificationAsk.asked = true
                    permission.launch(Manifest.permission.POST_NOTIFICATIONS)
                } else {
                    openNotificationSettings(context)
                }
            },
        )
    }

    if (picking) {
        AlertDialog(
            onDismissRequest = { picking = false },
            title = { Text(stringResource(R.string.push_pick)) },
            text = {
                Column {
                    view.distributors.forEach { pkg ->
                        TextButton(onClick = {
                            picking = false
                            enable(pkg)
                        }) { Text(UnifiedPush.label(context, pkg)) }
                    }
                }
            },
            confirmButton = {},
            dismissButton = { TextButton(onClick = { picking = false }) { Text(stringResource(R.string.cancel)) } },
        )
    }
}

@Composable
private fun Card(content: @Composable () -> Unit) {
    Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.Card).padding(16.dp)) { content() }
}

@Composable
private fun SwitchRow(title: String, checked: Boolean, enabled: Boolean, onChange: (Boolean) -> Unit, subtitle: String? = null) {
    Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
        Column(Modifier.weight(1f)) {
            Text(title, color = Palette.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium)
            subtitle?.let { Text(it, color = Palette.TextDim, fontSize = 11.sp) }
        }
        Switch(
            checked = checked,
            onCheckedChange = onChange,
            enabled = enabled,
            colors = SwitchDefaults.colors(checkedTrackColor = Palette.Accent),
        )
    }
}

/**
 * Turns push on by itself after the first QR pairing when exactly one distributor (usually ntfy) is
 * installed, and asks for the notification permission once — on any screen but pairing. This is the
 * Homebase behavior the user asked for: QR pairing alone sets up notifications (#222).
 */
@Composable
fun PushAutoSetup(store: PcStore) {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {}

    LaunchedEffect(Unit) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (true) {
                val v = readView(context, store)
                val pcCount = store.all().size
                var justEnabled = false
                if (pcCount > 0 && !v.userDisabled && v.distributor == null && v.distributors.size == 1) {
                    enablePush(context, v.distributors.single())
                    justEnabled = true
                } else if (v.distributor != null && v.distributor !in v.distributors) {
                    PushState(context).apply {
                        clear()
                        lastError = context.getString(R.string.push_err_removed)
                    }
                }
                if (pcCount > 0 && (justEnabled || v.endpoint != null) && Build.VERSION.SDK_INT >= 33 &&
                    !PushNotifier.canNotify(context) && !NotificationAsk.asked
                ) {
                    NotificationAsk.asked = true
                    permission.launch(Manifest.permission.POST_NOTIFICATIONS)
                }
                delay(2_000)
            }
        }
    }
}

private fun openUri(context: Context, primary: String, fallback: String?) {
    try {
        context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(primary)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    } catch (_: ActivityNotFoundException) {
        fallback?.let { context.startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(it)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }
}

private fun openNotificationSettings(context: Context) {
    context.startActivity(
        Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS)
            .putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
    )
}
