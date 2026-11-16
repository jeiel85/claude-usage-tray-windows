package io.github.jeiel85.claudeusage

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.ui.graphics.toArgb
import io.github.jeiel85.claudeusage.data.AppPrefs
import io.github.jeiel85.claudeusage.data.PcEntry
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.domain.PcConnection
import io.github.jeiel85.claudeusage.domain.PcStatus
import io.github.jeiel85.claudeusage.net.Gauge
import io.github.jeiel85.claudeusage.net.GaugeKind
import io.github.jeiel85.claudeusage.net.Endpoint
import io.github.jeiel85.claudeusage.net.NetworkProbe
import io.github.jeiel85.claudeusage.net.ProviderUsage
import io.github.jeiel85.claudeusage.net.Quota
import io.github.jeiel85.claudeusage.net.TodayTotals
import io.github.jeiel85.claudeusage.net.UsageApi
import io.github.jeiel85.claudeusage.net.UsageSnapshot
import io.github.jeiel85.claudeusage.push.NotificationHistory
import io.github.jeiel85.claudeusage.push.PushMessage
import io.github.jeiel85.claudeusage.push.PushNotifier
import io.github.jeiel85.claudeusage.ui.AppTheme
import io.github.jeiel85.claudeusage.ui.HomeScreen
import io.github.jeiel85.claudeusage.ui.NotificationCenterScreen
import io.github.jeiel85.claudeusage.ui.Palette
import io.github.jeiel85.claudeusage.ui.PairScreen
import io.github.jeiel85.claudeusage.ui.SettingsScreen
import java.time.LocalDate

/**
 * Debug builds only: the real screens filled with sample data, so the design can be checked (and screenshotted)
 * on an emulator without pairing a PC. Nothing here reaches a network or a PC.
 *
 * adb shell am start -n io.github.jeiel85.claudeusage/.DesignPreviewActivity -e screen accounts|pcs|history|settings|pair|empty|notify
 *
 * `notify` also posts the sample alerts as real Android notifications (needs the notification permission), to check
 * how they read in the shade.
 */
class DesignPreviewActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Palette.Bg.toArgb()),
            navigationBarStyle = SystemBarStyle.dark(Palette.Bg.toArgb()),
        )
        super.onCreate(savedInstanceState)
        AppPrefs.load(this)
        NotificationHistory.load(this)
        val now = System.currentTimeMillis()
        if (NotificationHistory.entries.value.isEmpty()) sampleAlerts(now).forEach { NotificationHistory.record(this, it) }

        val screen = intent.getStringExtra("screen") ?: "accounts"
        if (screen == "notify") sampleAlerts(now).forEach { PushNotifier.show(this, it) }
        val status = if (screen == "empty") emptyMap() else sampleStatus(now)
        val store = PcStore(this)
        val api = UsageApi(networks = NetworkProbe(this)::current)
        setContent {
            AppTheme {
                when (screen) {
                    "history" -> NotificationCenterScreen(onBack = ::finish)
                    "settings" -> SettingsScreen(store, api, onBack = ::finish)
                    "pair" -> PairScreen(store, api, onDone = {}, onBack = ::finish)
                    else -> HomeScreen(
                        status,
                        onAdd = {},
                        onRemove = {},
                        onSettings = {},
                        onHistory = {},
                        initialPage = if (screen == "pcs") 1 else 0,
                    )
                }
            }
        }
    }

    private fun sampleStatus(now: Long): Map<String, PcStatus> {
        val hour = 3_600_000L
        val today = LocalDate.now().toString()
        fun claude(observed: Long, short: Double, long: Double) = ProviderUsage(
            provider = "claude", accountHash = "a".repeat(64), accountScope = ProviderUsage.SCOPE_ACCOUNT,
            accountLabel = "jeiel@example.com", observedAtMs = observed, errorKind = "",
            quota = Quota(
                hasData = true, observedAtMs = observed, planType = "max",
                gauges = listOf(
                    Gauge(GaugeKind.SHORT, "", short, now + 2 * hour + 13 * 60_000, 300),
                    Gauge(GaugeKind.LONG, "", long, now + 3 * 24 * hour + 4 * hour, 7 * 24 * 60),
                    Gauge(GaugeKind.NAMED, "Opus", 0.94, now + 3 * 24 * hour + 4 * hour, 7 * 24 * 60),
                ),
                extraCreditsLabel = "",
            ),
            today = TodayTotals(today, 182_000, 64_000, 2_310_000, 140_000, 6, 220),
        )
        val codex = ProviderUsage(
            provider = "codex", accountHash = "b".repeat(64), accountScope = ProviderUsage.SCOPE_ACCOUNT,
            accountLabel = "", observedAtMs = now - 60_000, errorKind = "",
            quota = Quota(
                hasData = true, observedAtMs = now - 60_000, planType = "plus",
                gauges = listOf(
                    Gauge(GaugeKind.SHORT, "", 0.12, now + 4 * hour, 300),
                    Gauge(GaugeKind.LONG, "", 0.48, now + 5 * 24 * hour, 7 * 24 * 60),
                ),
                extraCreditsLabel = "",
            ),
            today = TodayTotals(today, 40_000, 9_000, 310_000, 0, 2, 41),
        )
        val antigravity = ProviderUsage(
            provider = "antigravity", accountHash = "c".repeat(64), accountScope = ProviderUsage.SCOPE_DEVICE,
            accountLabel = "", observedAtMs = now - 120_000, errorKind = "",
            quota = Quota(
                hasData = true, observedAtMs = now - 120_000, planType = "",
                gauges = listOf(Gauge(GaugeKind.NAMED, "Gemini 3 Pro", 0.76, now + 9 * hour, null)),
                extraCreditsLabel = "",
            ),
            today = null,
        )
        fun pc(id: String, name: String) = PcEntry(id, name, listOf(Endpoint(Endpoint.LAN, "192.168.0.10", 47821, true)), "dev", "token")
        val desk = pc("pc-desk", "DESKTOP-STUDIO")
        val laptop = pc("pc-laptop", "LAPTOP-TRAVEL")
        val office = pc("pc-office", "OFFICE-WS")
        return mapOf(
            desk.pcId to PcStatus(
                desk, PcConnection.LIVE, "LAN 192.168.0.10", null, null,
                UsageSnapshot(desk.pcId, desk.name, "1.58.2", now, listOf(claude(now - 30_000, 0.73, 0.41), codex, antigravity)),
                now - 30_000,
            ),
            laptop.pcId to PcStatus(
                laptop, PcConnection.RETRYING, null, "connect timed out", 12,
                UsageSnapshot(laptop.pcId, laptop.name, "1.58.1", now - 2 * hour, listOf(claude(now - 2 * hour, 0.6, 0.38))),
                now - 2 * hour,
            ),
            office.pcId to PcStatus(office, PcConnection.OFFLINE, null, null, null, null, null),
        )
    }

    private fun sampleAlerts(now: Long): List<PushMessage> = listOf(
        PushMessage("p1", "pc-desk", "DESKTOP-STUDIO", "usage", "critical", "Claude 90% · 5시간 한도", "15:30 초기화 · 2h 13m 남음", null, 0, null, now - 600_000,
            provider = "claude", accountHash = "a".repeat(64), accountLabel = "김민준 · Acme Corp"),
        PushMessage("p2", "pc-desk", "DESKTOP-STUDIO", "weather", "warning", "내일 아침 비 소식", "07시부터 강수확률 80%", null, 0, "https://example.com", now - 5 * 3_600_000),
        PushMessage("p3", "pc-laptop", "LAPTOP-TRAVEL", "reset", "info", "Codex 할당량 초기화됨", "이제 다시 Codex를 사용할 수 있습니다!", null, 0, null, now - 30 * 3_600_000,
            provider = "codex", accountHash = "b".repeat(64)),
    )
}
