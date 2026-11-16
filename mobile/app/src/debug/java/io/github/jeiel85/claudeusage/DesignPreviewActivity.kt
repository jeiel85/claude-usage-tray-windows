package io.github.jeiel85.claudeusage

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.ui.graphics.toArgb
import io.github.jeiel85.claudeusage.data.AppPrefs
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.domain.SampleData
import io.github.jeiel85.claudeusage.net.NetworkProbe
import io.github.jeiel85.claudeusage.net.UsageApi
import io.github.jeiel85.claudeusage.push.NotificationHistory
import io.github.jeiel85.claudeusage.push.PushMessage
import io.github.jeiel85.claudeusage.ui.AppTheme
import io.github.jeiel85.claudeusage.ui.HomeScreen
import io.github.jeiel85.claudeusage.ui.NotificationCenterScreen
import io.github.jeiel85.claudeusage.ui.Palette
import io.github.jeiel85.claudeusage.ui.PairScreen
import io.github.jeiel85.claudeusage.ui.SettingsScreen

/**
 * Debug builds only: the real screens filled with sample data, so the design can be checked (and screenshotted)
 * on an emulator without pairing a PC. Nothing here reaches a network or a PC.
 *
 * adb shell am start -n com.jeiel85.aiusage/io.github.jeiel85.claudeusage.DesignPreviewActivity -e screen accounts|pcs|history|settings|pair|empty
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
        val status = if (screen == "empty") emptyMap() else SampleData.status(now)
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

    private fun sampleAlerts(now: Long): List<PushMessage> = listOf(
        PushMessage("p1", "pc-desk", "DESKTOP-STUDIO", "usage", "critical", "Claude 5h 사용량 90%", "2h 13m 후 초기화", null, 0, null, now - 600_000),
        PushMessage("p2", "pc-desk", "DESKTOP-STUDIO", "weather", "warning", "내일 아침 비 소식", "07시부터 강수확률 80%", null, 0, "https://example.com", now - 5 * 3_600_000),
        PushMessage("p3", "pc-laptop", "LAPTOP-TRAVEL", "reset", "info", "Codex 사용량이 초기화되었습니다", null, null, 0, null, now - 30 * 3_600_000),
    )
}
