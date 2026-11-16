package io.github.jeiel85.claudeusage.ui

import android.text.format.DateUtils
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.pager.HorizontalPager
import androidx.compose.foundation.pager.rememberPagerState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.MoreVert
import androidx.compose.material.icons.filled.Notifications
import androidx.compose.material.icons.filled.Settings
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.BadgedBox
import androidx.compose.material3.Button
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableLongStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.drawWithContent
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.data.AppPrefs
import io.github.jeiel85.claudeusage.data.PcNames
import io.github.jeiel85.claudeusage.domain.AccountAggregator
import io.github.jeiel85.claudeusage.domain.AccountCard
import io.github.jeiel85.claudeusage.domain.PcConnection
import io.github.jeiel85.claudeusage.domain.PcStatus
import io.github.jeiel85.claudeusage.net.Gauge
import io.github.jeiel85.claudeusage.net.GaugeKind
import io.github.jeiel85.claudeusage.push.NotificationHistory
import io.github.jeiel85.claudeusage.push.NotificationHistoryLog
import io.github.jeiel85.claudeusage.update.AppUpdates
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

/**
 * Two tabs over the same live data: Accounts (what the user asked for — usage per account, merged
 * across PCs) and PCs (connection health and removal). Settings — notifications, display, updates,
 * about — open from the gear in the top bar (#230).
 *
 * Tabs are pages of a [HorizontalPager], so a sideways swipe moves between them as well as a tap on
 * the segmented control. Each page keeps its own list (and scroll position): swiping back to Accounts after
 * checking a PC returns to the same card instead of the top.
 */
@Composable
fun HomeScreen(
    status: Map<String, PcStatus>,
    onAdd: () -> Unit,
    onRemove: (String) -> Unit,
    /** This phone's own name for a PC (#271); a blank name clears it. */
    onRename: (pcId: String, alias: String) -> Unit,
    onSettings: () -> Unit,
    onHistory: () -> Unit,
    initialPage: Int = 0,
    /** Showing [io.github.jeiel85.claudeusage.domain.SampleData] instead of paired PCs (#261): a banner says so and offers [onEndSample]. */
    sample: Boolean = false,
    /** Empty screen's "Try with sample data"; null hides the button. */
    onTrySample: (() -> Unit)? = null,
    onEndSample: () -> Unit = {},
) {
    // rememberPagerState is saveable: the open tab survives rotation and process death like before.
    val pager = rememberPagerState(initialPage = initialPage, pageCount = { TAB_COUNT })
    val scope = rememberCoroutineScope()
    // Re-evaluate "resets in" / "x min ago" without waiting for the next frame from a PC.
    var now by remember { mutableLongStateOf(System.currentTimeMillis()) }
    LaunchedEffect(Unit) {
        while (true) {
            delay(30_000)
            now = System.currentTimeMillis()
        }
    }
    val pcs = status.values.sortedBy { it.pc.displayName.lowercase() }
    val cards = remember(status, now) { AccountAggregator.aggregate(pcs.map { it.toView() }, now) }
    val live = pcs.count { it.connection == PcConnection.LIVE }

    Column(Modifier.fillMaxSize().background(Palette.Bg).statusBarsPadding()) {
        Row(Modifier.fillMaxWidth().padding(start = 20.dp, end = 4.dp, top = 12.dp, bottom = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            Column(Modifier.weight(1f)) {
                Text(stringResource(R.string.app_name), color = Palette.Text, fontSize = 24.sp, fontWeight = FontWeight.Bold)
                if (pcs.isNotEmpty()) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Box(Modifier.size(7.dp).clip(CircleShape).background(if (live > 0) Palette.Ok else Palette.TextDim))
                        Spacer(Modifier.width(6.dp))
                        Text(stringResource(R.string.home_live_summary, live, pcs.size), color = Palette.TextSoft, fontSize = 12.sp)
                    }
                }
            }
            IconButton(onClick = onAdd) {
                Icon(Icons.Filled.Add, contentDescription = stringResource(R.string.add_pc), tint = Palette.TextSoft)
            }
            NotificationBell(onHistory)
            // Settings (#230): notifications, display, updates, about — the gear is where people look for them.
            IconButton(onClick = onSettings) {
                Icon(Icons.Filled.Settings, contentDescription = stringResource(R.string.settings_title), tint = Palette.TextSoft)
            }
        }
        // New app version (#228): one line above the tabs, only when there is something to install.
        AppUpdates.Banner(Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        // Old package still installed (#254): it would double every alert and never update into this app.
        LegacyAppNotice(Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        if (sample) SampleBanner(onEndSample, Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        if (pcs.isEmpty()) {
            EmptyState(onAdd, onTrySample)
            return@Column
        }
        SegmentedTabs(
            titles = listOf(stringResource(R.string.tab_accounts), stringResource(R.string.tab_pcs)),
            selected = pager.currentPage,
            onSelect = { scope.launch { pager.animateScrollToPage(it) } },
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 8.dp),
        )
        HorizontalPager(state = pager, modifier = Modifier.fillMaxSize(), key = { it }) { page ->
            LazyColumn(
                Modifier.fillMaxSize().navigationBarsPadding(),
                contentPadding = PaddingValues(start = 16.dp, end = 16.dp, top = 8.dp, bottom = 24.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
            ) {
                when (page) {
                    TAB_ACCOUNTS -> {
                        if (cards.isEmpty()) item { Hint(stringResource(R.string.no_usage_yet)) }
                        items(cards, key = { it.key }) { AccountCardView(it, now) }
                    }
                    else -> {
                        items(pcs, key = { it.pc.pcId }) { PcRow(it, now, onRemove, onRename) }
                        item(key = "add") { AddPcRow(onAdd) }
                    }
                }
            }
        }
    }
}

/** Notification center (#235) with the number of alerts that arrived since it was last opened. */
@Composable
private fun NotificationBell(onClick: () -> Unit) {
    val entries by NotificationHistory.entries.collectAsStateWithLifecycle()
    val lastSeen by NotificationHistory.lastSeenMs.collectAsStateWithLifecycle()
    val unread = NotificationHistoryLog.unreadCount(entries, lastSeen)
    val description = if (unread > 0) pluralStringResource(R.plurals.history_unread, unread, unread) else stringResource(R.string.history_title)
    IconButton(onClick = onClick) {
        BadgedBox(badge = {
            if (unread > 0) Badge(containerColor = Palette.Accent) { Text(if (unread > 99) "99+" else unread.toString()) }
        }) {
            Icon(Icons.Filled.Notifications, contentDescription = description, tint = Palette.TextSoft)
        }
    }
}

private const val TAB_ACCOUNTS = 0
private const val TAB_COUNT = 2

/**
 * Input : 초기화 시각, 지금(epoch ms)
 * Output: "2h 13m 후 초기화" 또는 (설정 → 화면) "오후 3:20 초기화"·"11월 16일 (일) 오전 9:00 초기화"
 * 핵심 로직: 시각 표시는 기기의 12/24시간 설정과 언어를 따른다(DateUtils). 오늘이 아니면 날짜를 붙인다 — 7일 창의 초기화는
 *           며칠 뒤라 시각만으로는 언제인지 알 수 없다.
 */
@Composable
private fun resetText(resetAtMs: Long, now: Long): String {
    if (!AppPrefs.absoluteResetTime) return stringResource(R.string.reset_in, compactDuration(resetAtMs - now))
    val context = LocalContext.current
    val flags = if (DateUtils.isToday(resetAtMs)) {
        DateUtils.FORMAT_SHOW_TIME
    } else {
        DateUtils.FORMAT_SHOW_TIME or DateUtils.FORMAT_SHOW_DATE or DateUtils.FORMAT_ABBREV_MONTH or
            DateUtils.FORMAT_SHOW_WEEKDAY or DateUtils.FORMAT_ABBREV_WEEKDAY
    }
    return stringResource(R.string.reset_at, DateUtils.formatDateTime(context, resetAtMs, flags))
}

/** Sample mode (#261): the cards below are made up, not the user's PCs. */
@Composable
private fun SampleBanner(onEnd: () -> Unit, modifier: Modifier = Modifier) {
    Row(
        modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Palette.Warn.copy(alpha = 0.18f))
            .padding(start = 16.dp, end = 4.dp, top = 4.dp, bottom = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            stringResource(R.string.sample_banner),
            color = Palette.Text,
            fontSize = 12.sp,
            lineHeight = 17.sp,
            modifier = Modifier.weight(1f).padding(vertical = 8.dp),
        )
        TextButton(onClick = onEnd) { Text(stringResource(R.string.sample_end)) }
    }
}

@Composable
private fun EmptyState(onAdd: () -> Unit, onTrySample: (() -> Unit)?) {
    Column(Modifier.fillMaxSize().padding(32.dp), verticalArrangement = Arrangement.Center, horizontalAlignment = Alignment.CenterHorizontally) {
        Box(
            Modifier.size(64.dp).clip(RoundedCornerShape(20.dp)).background(Palette.Accent.copy(alpha = 0.16f)),
            contentAlignment = Alignment.Center,
        ) {
            Icon(Icons.Filled.Add, contentDescription = null, tint = Palette.AccentLight, modifier = Modifier.size(32.dp))
        }
        Spacer(Modifier.height(20.dp))
        Text(stringResource(R.string.empty_title), color = Palette.Text, fontSize = 19.sp, fontWeight = FontWeight.SemiBold)
        Spacer(Modifier.height(10.dp))
        Text(stringResource(R.string.empty_body), color = Palette.TextSoft, fontSize = 14.sp, lineHeight = 20.sp, textAlign = TextAlign.Center)
        Spacer(Modifier.height(24.dp))
        Button(onClick = onAdd, modifier = Modifier.height(48.dp)) { Text(stringResource(R.string.add_pc), fontSize = 15.sp) }
        if (onTrySample != null) {
            Spacer(Modifier.height(8.dp))
            TextButton(onClick = onTrySample) { Text(stringResource(R.string.sample_try), fontSize = 14.sp) }
        }
    }
}

@Composable
private fun Hint(text: String) {
    Text(text, color = Palette.TextSoft, fontSize = 13.sp, modifier = Modifier.padding(8.dp))
}

/**
 * One account across every PC that reports it. Monogram, name and plan on top; one two-line row per window
 * (name and reset time with the percentage at the right, the bar below); then today's totals and the PCs.
 */
@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun AccountCardView(card: AccountCard, now: Long) {
    val context = LocalContext.current
    val name = providerName(card.provider)
    Card {
        Column(verticalArrangement = Arrangement.spacedBy(16.dp)) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Monogram(name.take(1), Palette.forProvider(card.provider))
                Spacer(Modifier.width(12.dp))
                Column(Modifier.weight(1f)) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(name, color = Palette.Text, fontSize = 17.sp, fontWeight = FontWeight.SemiBold)
                        if (card.planType.isNotBlank()) {
                            Spacer(Modifier.width(8.dp))
                            Chip(card.planType.uppercase(), Palette.AccentLight)
                        }
                    }
                    if (card.label.isNotBlank()) {
                        Text(card.label, color = Palette.TextSoft, fontSize = 12.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
                    }
                    if (!card.accountScoped) {
                        Text(stringResource(R.string.device_scope_note), color = Palette.TextDim, fontSize = 11.sp)
                    }
                }
            }

            if (card.gauges.isEmpty()) {
                Text(stringResource(R.string.no_quota), color = Palette.TextDim, fontSize = 12.sp)
            } else {
                Column(verticalArrangement = Arrangement.spacedBy(14.dp)) { card.gauges.forEach { GaugeRow(it, now) } }
            }
            card.quota?.extraCreditsLabel?.takeIf { it.isNotBlank() }?.let {
                Text(it, color = Palette.TextSoft, fontSize = 12.sp)
            }

            HorizontalDivider(color = Palette.CardBorder)
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                card.today?.let {
                    Text(
                        stringResource(R.string.today_tokens, compactNumber(it.totalTokens), it.sessionCount),
                        color = Palette.TextSoft,
                        fontSize = 13.sp,
                    )
                }
                FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                    card.devices.forEach { d -> DeviceChip(d.pcName, d.online) }
                }
                if (card.quotaFromPcName != null && card.quotaObservedAtMs != null && card.devices.size > 1) {
                    Text(
                        stringResource(R.string.quota_from, card.quotaFromPcName, ago(context, card.quotaObservedAtMs, now)),
                        color = Palette.TextDim,
                        fontSize = 11.sp,
                    )
                }
            }
        }
    }
}

@Composable
private fun DeviceChip(name: String, online: Boolean) {
    Row(
        Modifier.clip(RoundedCornerShape(50)).background(Palette.Raised).padding(horizontal = 10.dp, vertical = 5.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Box(Modifier.size(6.dp).clip(CircleShape).background(if (online) Palette.Ok else Palette.TextDim))
        Spacer(Modifier.width(6.dp))
        Text(name, color = if (online) Palette.Text else Palette.TextSoft, fontSize = 11.sp, maxLines = 1)
    }
}

/**
 * Input : 게이지 1개, 지금(epoch ms)
 * Output: 두 줄 — 위는 (창 이름 + 초기화 시각 | 퍼센트), 아래는 막대
 * 핵심 로직: 초기화 문구를 이름과 같은 줄에 붙여 세 줄을 두 줄로 줄인다. 긴 문구(날짜가 붙은 시각, 긴 모델 이름)는 말줄임 없이
 *           줄바꿈한다 — 시각이 잘리면 언제 초기화되는지 알 수 없다. 퍼센트는 70% 부터 막대와 같은 색으로 바뀐다.
 */
@Composable
private fun GaugeRow(g: Gauge, now: Long) {
    val label = when (g.kind) {
        GaugeKind.SHORT, GaugeKind.LONG -> windowLabel(g.windowMinutes) ?: if (g.kind == GaugeKind.SHORT) "5h" else "7d"
        GaugeKind.EXTRA -> stringResource(R.string.gauge_extra)
        GaugeKind.NAMED -> g.name
    }
    val reset = g.resetAtMs?.let { resetText(it, now) }
    val text = buildAnnotatedString {
        withStyle(SpanStyle(color = Palette.Text, fontWeight = FontWeight.Medium, fontSize = 14.sp)) { append(label) }
        if (reset != null) {
            append("   ")
            withStyle(SpanStyle(color = Palette.TextDim, fontSize = 12.sp)) { append(reset) }
        }
    }
    Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
        Row(verticalAlignment = Alignment.Bottom) {
            Text(text, modifier = Modifier.weight(1f))
            Spacer(Modifier.width(12.dp))
            Text(
                "${(g.usage * 100).toInt()}%",
                color = Palette.usageText(g.usage),
                fontSize = 17.sp,
                fontWeight = FontWeight.Bold,
                style = TextStyle(fontFeatureSettings = "tnum"),
            )
        }
        UsageBar(g.usage, g.timeProgress(now))
    }
}

/**
 * Input : 사용률(0..1), 창 경과율(0..1, 모르면 null)
 * Output: 막대와 그 위의 시간선 — PC 팝업과 같은 뜻의 세로 마커(#269). 막대가 마커보다 앞서면 이번 창을 평소보다 빨리 쓰는 중.
 * 핵심 로직: 마커는 막대 위아래로 3dp 넘치게 그려 채워진 부분 위에서도 보이게 하고, 카드색 테두리로 막대색과 떼어 놓는다.
 *           바깥 Box 는 자르지 않고(넘친 부분이 보이도록) 막대 Box 만 둥글게 자른다 — 줄 높이는 그대로 8dp.
 */
@Composable
private fun UsageBar(usage: Double, timeProgress: Double?) {
    val fraction = usage.toFloat().coerceIn(0f, 1f)
    val marker = timeProgress?.toFloat()?.coerceIn(0f, 1f)
    Box(
        Modifier
            .fillMaxWidth()
            .height(8.dp)
            .drawWithContent {
                drawContent()
                if (marker != null) {
                    val overhang = 3.dp.toPx()
                    val halo = 4.dp.toPx()
                    val x = (size.width * marker).coerceIn(halo / 2, size.width - halo / 2)
                    val top = Offset(x, -overhang)
                    val bottom = Offset(x, size.height + overhang)
                    drawLine(Palette.Card, top, bottom, strokeWidth = halo, cap = StrokeCap.Round)
                    drawLine(Palette.Text, top, bottom, strokeWidth = 2.dp.toPx(), cap = StrokeCap.Round)
                }
            },
    ) {
        Box(Modifier.fillMaxSize().clip(RoundedCornerShape(50)).background(Palette.Track)) {
            if (fraction > 0f) {
                Box(
                    Modifier
                        .fillMaxWidth(fraction)
                        .widthIn(min = 8.dp)
                        .fillMaxHeight()
                        .clip(RoundedCornerShape(50))
                        .background(Palette.forUsage(usage)),
                )
            }
        }
    }
}

/** One paired PC: its state as a coloured chip, details below, rename and removal behind the overflow menu. */
@Composable
private fun PcRow(s: PcStatus, now: Long, onRemove: (String) -> Unit, onRename: (String, String) -> Unit) {
    val context = LocalContext.current
    var menu by remember { mutableStateOf(false) }
    var confirm by remember { mutableStateOf(false) }
    var renaming by remember { mutableStateOf(false) }
    val (color, line) = when (s.connection) {
        PcConnection.LIVE -> Palette.Ok to stringResource(R.string.pc_live, s.endpointLabel ?: "")
        PcConnection.CONNECTING -> Palette.Warn to stringResource(R.string.pc_connecting)
        PcConnection.RETRYING -> Palette.Warn to stringResource(R.string.pc_retrying, s.retryInSeconds ?: 0)
        PcConnection.UNAUTHORIZED -> Palette.Danger to stringResource(R.string.pc_unauthorized)
        PcConnection.INCOMPATIBLE -> Palette.Danger to stringResource(R.string.pc_incompatible)
        PcConnection.OFFLINE -> Palette.TextSoft to stringResource(R.string.pc_offline)
    }
    Card {
        Row(verticalAlignment = Alignment.Top) {
            Column(Modifier.weight(1f).padding(top = 2.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Column {
                    Text(s.pc.displayName, color = Palette.Text, fontSize = 16.sp, fontWeight = FontWeight.SemiBold)
                    // With an alias the PC's own name still matters: it is what the PC and its other devices show.
                    if (s.pc.alias != null) {
                        Text(stringResource(R.string.pc_own_name, s.pc.name), color = Palette.TextDim, fontSize = 12.sp)
                    }
                }
                Chip(line, color, dot = true)
                if (s.connection == PcConnection.RETRYING && !s.detail.isNullOrBlank()) {
                    Text(s.detail, color = Palette.TextDim, fontSize = 11.sp, maxLines = 2, overflow = TextOverflow.Ellipsis)
                }
                val parts = buildList {
                    s.receivedAtMs?.let { add(context.getString(R.string.pc_last_received, ago(context, it, now))) }
                    s.snapshot?.let { add(context.getString(R.string.pc_providers, it.providers.size)) }
                    s.snapshot?.appVersion?.takeIf { it.isNotBlank() }?.let { add("v$it") }
                }
                if (parts.isNotEmpty()) Text(parts.joinToString(" · "), color = Palette.TextDim, fontSize = 12.sp)
            }
            Box {
                IconButton(onClick = { menu = true }, modifier = Modifier.offset(x = 8.dp, y = (-8).dp)) {
                    Icon(Icons.Filled.MoreVert, contentDescription = stringResource(R.string.more_options), tint = Palette.TextSoft)
                }
                // A raised surface with a border and shadow: in the card's own colour the menu read as a hole cut into the
                // card, clipping the line under it.
                DropdownMenu(
                    expanded = menu,
                    onDismissRequest = { menu = false },
                    shape = RoundedCornerShape(12.dp),
                    containerColor = Palette.Raised,
                    border = BorderStroke(1.dp, Palette.Line),
                    shadowElevation = 8.dp,
                ) {
                    DropdownMenuItem(
                        text = { Text(stringResource(R.string.rename_pc), color = Palette.Text, fontSize = 15.sp) },
                        leadingIcon = { Icon(Icons.Filled.Edit, contentDescription = null, tint = Palette.TextSoft) },
                        onClick = {
                            menu = false
                            renaming = true
                        },
                    )
                    DropdownMenuItem(
                        text = { Text(stringResource(R.string.remove_pc), color = Palette.Danger, fontSize = 15.sp) },
                        leadingIcon = { Icon(Icons.Filled.Delete, contentDescription = null, tint = Palette.Danger) },
                        onClick = {
                            menu = false
                            confirm = true
                        },
                    )
                }
            }
        }
    }
    if (confirm) {
        AlertDialog(
            onDismissRequest = { confirm = false },
            text = { Text(stringResource(R.string.remove_confirm, s.pc.displayName)) },
            confirmButton = {
                TextButton(onClick = {
                    confirm = false
                    onRemove(s.pc.pcId)
                }) { Text(stringResource(R.string.remove_pc), color = Palette.Danger) }
            },
            dismissButton = { TextButton(onClick = { confirm = false }) { Text(stringResource(R.string.cancel)) } },
        )
    }
    if (renaming) {
        RenameDialog(
            current = s.pc.alias.orEmpty(),
            pcName = s.pc.name,
            onDismiss = { renaming = false },
            onSave = { alias ->
                renaming = false
                onRename(s.pc.pcId, alias)
            },
        )
    }
}

/**
 * This phone's name for a PC (#271). Empty means "use the PC's name", which the field shows as its placeholder —
 * so clearing the field is how the alias goes away, without a separate reset button.
 */
@Composable
private fun RenameDialog(current: String, pcName: String, onDismiss: () -> Unit, onSave: (String) -> Unit) {
    var text by remember { mutableStateOf(current) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.rename_pc_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
                OutlinedTextField(
                    value = text,
                    onValueChange = { if (it.length <= PcNames.MAX_ALIAS) text = it },
                    placeholder = { Text(pcName, maxLines = 1, overflow = TextOverflow.Ellipsis) },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                Text(stringResource(R.string.rename_pc_hint), color = Palette.TextDim, fontSize = 12.sp)
            }
        },
        confirmButton = { TextButton(onClick = { onSave(text) }) { Text(stringResource(R.string.save)) } },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.cancel)) } },
    )
}

/** The last row of the PC list: the same action as the + in the top bar, where a list of PCs ends. */
@Composable
private fun AddPcRow(onAdd: () -> Unit) {
    Box(
        Modifier
            .fillMaxWidth()
            .clip(CardShape)
            .border(1.dp, Palette.Line, CardShape)
            .clickable(role = Role.Button, onClick = onAdd)
            .padding(vertical = 16.dp),
        contentAlignment = Alignment.Center,
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Icon(Icons.Filled.Add, contentDescription = null, tint = Palette.AccentLight, modifier = Modifier.size(20.dp))
            Spacer(Modifier.width(8.dp))
            Text(stringResource(R.string.add_pc), color = Palette.AccentLight, fontSize = 14.sp, fontWeight = FontWeight.SemiBold)
        }
    }
}
