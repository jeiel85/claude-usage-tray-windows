package io.github.jeiel85.claudeusage.ui

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.text.format.DateUtils
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.push.HistoryEntry
import io.github.jeiel85.claudeusage.push.NotificationGroups
import io.github.jeiel85.claudeusage.push.NotificationHistory

/**
 * Notification center (#235): the alerts this phone received, newest first, so one swiped away in the
 * Android shade (or never shown because notifications were off) can still be read. Opened from the bell
 * above the home tabs. Alerts that arrived since the last visit are marked; opening and leaving the screen
 * both count as having seen them.
 */
@Composable
fun NotificationCenterScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val entries by NotificationHistory.entries.collectAsStateWithLifecycle()
    // What was unread when the screen opened stays marked while it is open; arrivals during the visit are new too.
    val seenBefore = rememberSaveable { NotificationHistory.lastSeenMs.value }
    var confirmClear by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { NotificationHistory.markAllRead(context) }
    DisposableEffect(Unit) { onDispose { NotificationHistory.markAllRead(context) } }

    Column(Modifier.fillMaxSize().background(Palette.Bg).statusBarsPadding()) {
        Row(Modifier.fillMaxWidth().padding(start = 4.dp, end = 8.dp, top = 8.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = stringResource(R.string.back), tint = Palette.Text)
            }
            Text(
                stringResource(R.string.history_title),
                color = Palette.Text,
                fontSize = 20.sp,
                fontWeight = FontWeight.SemiBold,
                modifier = Modifier.weight(1f),
            )
            if (entries.isNotEmpty()) {
                TextButton(onClick = { confirmClear = true }) { Text(stringResource(R.string.history_clear), color = Palette.TextSoft) }
            }
        }
        if (entries.isEmpty()) {
            Column(
                Modifier.fillMaxSize().padding(32.dp),
                verticalArrangement = Arrangement.Center,
                horizontalAlignment = Alignment.CenterHorizontally,
            ) {
                Text(stringResource(R.string.history_empty_title), color = Palette.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
                Spacer(Modifier.height(10.dp))
                Text(stringResource(R.string.history_empty_body), color = Palette.TextSoft, fontSize = 13.sp)
            }
        } else {
            LazyColumn(
                Modifier.fillMaxSize().navigationBarsPadding(),
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                items(entries, key = { "${it.id}|${it.receivedAtMs}" }) { entry ->
                    HistoryRow(entry, unread = entry.receivedAtMs > seenBefore)
                }
                item {
                    Text(
                        stringResource(R.string.history_retention),
                        color = Palette.TextDim,
                        fontSize = 11.sp,
                        modifier = Modifier.padding(horizontal = 4.dp, vertical = 8.dp),
                    )
                }
            }
        }
    }

    if (confirmClear) {
        AlertDialog(
            onDismissRequest = { confirmClear = false },
            text = { Text(stringResource(R.string.history_clear_confirm)) },
            confirmButton = {
                TextButton(onClick = {
                    confirmClear = false
                    NotificationHistory.clear(context)
                }) { Text(stringResource(R.string.history_clear)) }
            },
            dismissButton = { TextButton(onClick = { confirmClear = false }) { Text(stringResource(R.string.cancel)) } },
        )
    }
}

@Composable
private fun HistoryRow(entry: HistoryEntry, unread: Boolean) {
    val context = LocalContext.current
    val dot = when (entry.priority) {
        "critical" -> Palette.Danger
        "warning" -> Palette.Warn
        else -> Palette.AccentLight
    }
    val url = entry.clickUrl
    Box(
        Modifier.fillMaxWidth()
            .clip(RoundedCornerShape(14.dp))
            .background(Palette.Card)
            .then(if (url != null) Modifier.clickable { context.openLink(url) } else Modifier)
            .padding(16.dp),
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(8.dp).clip(CircleShape).background(dot))
                Spacer(Modifier.width(8.dp))
                val meta = listOf(stringResource(NotificationGroups.label(entry.category)), entry.pcName).filter { it.isNotBlank() }
                Text(meta.joinToString(" · "), color = Palette.TextSoft, fontSize = 11.sp, modifier = Modifier.weight(1f), maxLines = 1)
                if (unread) {
                    Text(
                        stringResource(R.string.history_new),
                        color = Palette.AccentLight,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.clip(RoundedCornerShape(6.dp)).background(Palette.Line).padding(horizontal = 6.dp, vertical = 2.dp),
                    )
                    Spacer(Modifier.width(6.dp))
                }
                Text(eventTime(context, entry.atMs), color = Palette.TextDim, fontSize = 11.sp)
            }
            Text(entry.title, color = Palette.Text, fontSize = 14.sp, fontWeight = if (unread) FontWeight.SemiBold else FontWeight.Medium)
            entry.body?.let { Text(it, color = Palette.TextSoft, fontSize = 12.sp) }
            if (url != null) Text(stringResource(R.string.history_open_link), color = Palette.AccentLight, fontSize = 12.sp)
        }
    }
}

/** Today: the time only; otherwise the date too — a history spans up to 30 days. Follows the device's 12/24 h and language. */
private fun eventTime(context: Context, ms: Long): String {
    val flags = if (DateUtils.isToday(ms)) {
        DateUtils.FORMAT_SHOW_TIME
    } else {
        DateUtils.FORMAT_SHOW_TIME or DateUtils.FORMAT_SHOW_DATE or DateUtils.FORMAT_ABBREV_MONTH
    }
    return DateUtils.formatDateTime(context, ms, flags)
}

private fun Context.openLink(url: String) {
    runCatching { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
}
