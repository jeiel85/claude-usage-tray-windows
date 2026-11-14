package io.github.jeiel85.claudeusage.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.ExperimentalLayoutApi
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
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.Tab
import androidx.compose.material3.TabRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableLongStateOf
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.domain.AccountAggregator
import io.github.jeiel85.claudeusage.domain.AccountCard
import io.github.jeiel85.claudeusage.domain.PcConnection
import io.github.jeiel85.claudeusage.domain.PcStatus
import io.github.jeiel85.claudeusage.net.Gauge
import io.github.jeiel85.claudeusage.net.GaugeKind
import kotlinx.coroutines.delay

/**
 * Two tabs over the same live data: Accounts (what the user asked for — usage per account, merged
 * across PCs) and PCs (connection health and removal).
 */
@Composable
fun HomeScreen(status: Map<String, PcStatus>, onAdd: () -> Unit, onRemove: (String) -> Unit) {
    var tab by rememberSaveable { mutableIntStateOf(0) }
    // Re-evaluate "resets in" / "x min ago" without waiting for the next frame from a PC.
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(30_000)
            now = System.currentTimeMillis()
        }
    }
    val pcs = status.values.sortedBy { it.pc.name.lowercase() }
    val cards = remember(status, now) { AccountAggregator.aggregate(pcs.map { it.toView() }, now) }

    Column(Modifier.fillMaxSize().background(Palette.Bg).statusBarsPadding()) {
        Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 8.dp, top = 12.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.app_name), color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
            TextButton(onClick = onAdd) { Text("+ " + stringResource(R.string.add_pc), color = Palette.AccentLight) }
        }
        TabRow(selectedTabIndex = tab, containerColor = Palette.Bg, contentColor = Palette.AccentLight) {
            Tab(selected = tab == 0, onClick = { tab = 0 }, text = { Text(stringResource(R.string.tab_accounts)) })
            Tab(selected = tab == 1, onClick = { tab = 1 }, text = { Text(stringResource(R.string.tab_pcs)) })
        }
        if (pcs.isEmpty()) {
            EmptyState(onAdd)
            return@Column
        }
        LazyColumn(
            Modifier.fillMaxSize().navigationBarsPadding(),
            contentPadding = PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (tab == 0) {
                if (cards.isEmpty()) item { Hint(stringResource(R.string.no_usage_yet)) }
                items(cards, key = { it.key }) { AccountCardView(it, now) }
            } else {
                items(pcs, key = { it.pc.pcId }) { PcRow(it, now, onRemove) }
            }
        }
    }
}

@Composable
private fun EmptyState(onAdd: () -> Unit) {
    Column(Modifier.fillMaxSize().padding(32.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
        Text(stringResource(R.string.empty_title), color = Palette.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(10.dp))
        Text(stringResource(R.string.empty_body), color = Palette.TextSoft, fontSize = 13.sp)
        Spacer(Modifier.height(20.dp))
        Button(onClick = onAdd) { Text(stringResource(R.string.add_pc)) }
    }
}

@Composable
private fun Hint(text: String) {
    Text(text, color = Palette.TextSoft, fontSize = 13.sp, modifier = Modifier.padding(8.dp))
}

@Composable
private fun CardBox(content: @Composable () -> Unit) {
    Box(Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.Card).padding(16.dp)) { content() }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AccountCardView(card: AccountCard, now: Long) {
    val context = LocalContext.current
    CardBox {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Text(providerName(card.provider), color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                if (card.planType.isNotBlank()) {
                    Spacer(Modifier.width(8.dp))
                    Text(
                        card.planType.uppercase(),
                        color = Palette.AccentLight,
                        fontSize = 10.sp,
                        fontWeight = FontWeight.SemiBold,
                        modifier = Modifier.clip(RoundedCornerShape(6.dp)).background(Palette.Line).padding(horizontal = 6.dp, vertical = 2.dp),
                    )
                }
                if (card.label.isNotBlank()) {
                    Spacer(Modifier.width(8.dp))
                    Text(card.label, color = Palette.TextSoft, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                }
            }
            if (!card.accountScoped) Text(stringResource(R.string.device_scope_note), color = Palette.TextDim, fontSize = 11.sp)

            if (card.gauges.isEmpty()) {
                Text(stringResource(R.string.no_quota), color = Palette.TextDim, fontSize = 12.sp)
            } else {
                card.gauges.forEach { GaugeRow(it, now) }
            }
            card.quota?.extraCreditsLabel?.takeIf { it.isNotBlank() }?.let {
                Text(it, color = Palette.TextSoft, fontSize = 11.sp)
            }
            card.today?.let {
                Text(stringResource(R.string.today_tokens, compactNumber(it.totalTokens), it.sessionCount), color = Palette.TextSoft, fontSize = 12.sp)
            }

            FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                card.devices.forEach { d ->
                    Row(
                        Modifier.clip(RoundedCornerShape(10.dp)).background(Palette.Bg).padding(horizontal = 8.dp, vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Box(Modifier.size(7.dp).clip(CircleShape).background(if (d.online) Palette.Ok else Palette.TextDim))
                        Spacer(Modifier.width(6.dp))
                        Text(d.pcName, color = Palette.Text, fontSize = 11.sp)
                    }
                }
            }
            if (card.quotaFromPcName != null && card.quotaObservedAtMs != null && card.devices.size > 1) {
                Text(
                    stringResource(R.string.quota_from, card.quotaFromPcName, ago(context, card.quotaObservedAtMs, now)),
                    color = Palette.TextDim,
                    fontSize = 10.sp,
                )
            }
        }
    }
}

@Composable
private fun GaugeRow(g: Gauge, now: Long) {
    val label = when (g.kind) {
        GaugeKind.SHORT, GaugeKind.LONG -> windowLabel(g.windowMinutes) ?: if (g.kind == GaugeKind.SHORT) "5h" else "7d"
        GaugeKind.EXTRA -> stringResource(R.string.gauge_extra)
        GaugeKind.NAMED -> g.name
    }
    Column(verticalArrangement = Arrangement.spacedBy(3.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(label, color = Palette.TextSoft, fontSize = 12.sp, modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text("${(g.usage * 100).toInt()}%", color = Palette.Text, fontSize = 13.sp, fontWeight = FontWeight.SemiBold)
        }
        LinearProgressIndicator(
            progress = { g.usage.toFloat() },
            modifier = Modifier.fillMaxWidth().height(6.dp).clip(RoundedCornerShape(3.dp)),
            color = Palette.forUsage(g.usage),
            trackColor = Palette.Line,
            drawStopIndicator = {},
            gapSize = 0.dp,
        )
        g.resetAtMs?.let { Text(stringResource(R.string.reset_in, compactDuration(it - now)), color = Palette.TextDim, fontSize = 10.sp) }
    }
}

@Composable
private fun PcRow(s: PcStatus, now: Long, onRemove: (String) -> Unit) {
    val context = LocalContext.current
    var confirm by remember { mutableStateOf(false) }
    val (dot, line) = when (s.connection) {
        PcConnection.LIVE -> Palette.Ok to stringResource(R.string.pc_live, s.endpointLabel ?: "")
        PcConnection.CONNECTING -> Palette.Warn to stringResource(R.string.pc_connecting)
        PcConnection.RETRYING -> Palette.Warn to stringResource(R.string.pc_retrying, s.retryInSeconds ?: 0)
        PcConnection.UNAUTHORIZED -> Palette.Danger to stringResource(R.string.pc_unauthorized)
        PcConnection.INCOMPATIBLE -> Palette.Danger to stringResource(R.string.pc_incompatible)
        PcConnection.OFFLINE -> Palette.TextDim to stringResource(R.string.pc_offline)
    }
    CardBox {
        Column(verticalArrangement = Arrangement.spacedBy(4.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Box(Modifier.size(9.dp).clip(CircleShape).background(dot))
                Spacer(Modifier.width(8.dp))
                Text(s.pc.name, color = Palette.Text, fontSize = 15.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
                TextButton(onClick = { confirm = true }) { Text(stringResource(R.string.remove_pc), color = Palette.TextSoft, fontSize = 12.sp) }
            }
            Text(line, color = Palette.TextSoft, fontSize = 12.sp)
            if (s.connection == PcConnection.RETRYING && !s.detail.isNullOrBlank()) {
                Text(s.detail, color = Palette.TextDim, fontSize = 10.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
            }
            val parts = buildList {
                s.receivedAtMs?.let { add(context.getString(R.string.pc_last_received, ago(context, it, now))) }
                s.snapshot?.let { add(context.getString(R.string.pc_providers, it.providers.size)) }
                s.snapshot?.appVersion?.takeIf { it.isNotBlank() }?.let { add("v$it") }
            }
            if (parts.isNotEmpty()) Text(parts.joinToString(" · "), color = Palette.TextDim, fontSize = 11.sp)
        }
    }
    if (confirm) {
        AlertDialog(
            onDismissRequest = { confirm = false },
            text = { Text(stringResource(R.string.remove_confirm, s.pc.name)) },
            confirmButton = {
                TextButton(onClick = {
                    confirm = false
                    onRemove(s.pc.pcId)
                }) { Text(stringResource(R.string.remove_pc)) }
            },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text(stringResource(R.string.cancel)) } },
        )
    }
}
