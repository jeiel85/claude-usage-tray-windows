package io.github.jeiel85.claudeusage.ui

import android.content.Context
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import io.github.jeiel85.claudeusage.R

/** Same palette as the PC tray popup, so the phone reads as the same product. */
object Palette {
    val Bg = Color(0xFF0F1117)
    val Card = Color(0xFF1A1D2E)
    val Line = Color(0xFF2D2F45)
    val Text = Color(0xFFF1F5F9)
    val TextSoft = Color(0xFF8B92B3)
    val TextDim = Color(0xFF5A5F7A)
    val Accent = Color(0xFF8B5CF6)
    val AccentLight = Color(0xFFA78BFA)
    val Ok = Color(0xFF10B981)
    val Warn = Color(0xFFF59E0B)
    val Danger = Color(0xFFEF4444)

    /** Bar colour by how much of the window is used — the same thresholds the tray gauge uses visually. */
    fun forUsage(usage: Double): Color = when {
        usage >= 0.9 -> Danger
        usage >= 0.7 -> Warn
        else -> Accent
    }
}

@Composable
fun AppTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = darkColorScheme(
            primary = Palette.Accent,
            onPrimary = Color.White,
            background = Palette.Bg,
            onBackground = Palette.Text,
            surface = Palette.Bg,
            onSurface = Palette.Text,
            surfaceVariant = Palette.Card,
            onSurfaceVariant = Palette.TextSoft,
            outline = Palette.Line,
        ),
        content = content,
    )
}

/** Provider names are product names — the same in every language. */
fun providerName(provider: String): String = when (provider) {
    "claude" -> "Claude"
    "codex" -> "Codex"
    "opencode" -> "OpenCode"
    "antigravity" -> "Antigravity"
    else -> provider.replaceFirstChar { it.uppercase() }
}

/** "2h 13m", "3d 4h", "45m" — unit letters only, so no translation is needed. */
fun compactDuration(ms: Long): String {
    val minutes = (ms / 60_000).coerceAtLeast(0)
    val days = minutes / (24 * 60)
    val hours = (minutes / 60) % 24
    val mins = minutes % 60
    return when {
        days > 0 -> if (hours > 0) "${days}d ${hours}h" else "${days}d"
        hours > 0 -> if (mins > 0) "${hours}h ${mins}m" else "${hours}h"
        else -> "${mins.coerceAtLeast(1)}m"
    }
}

/** Window length as a short label: 300 → "5h", 10080 → "7d". */
fun windowLabel(minutes: Int?): String? = when {
    minutes == null || minutes <= 0 -> null
    minutes % (24 * 60) == 0 -> "${minutes / (24 * 60)}d"
    minutes % 60 == 0 -> "${minutes / 60}h"
    else -> "${minutes}m"
}

/** "just now", "5 min ago", … (localized). */
fun ago(context: Context, thenMs: Long, nowMs: Long): String {
    val minutes = ((nowMs - thenMs) / 60_000).coerceAtLeast(0).toInt()
    return when {
        minutes < 1 -> context.getString(R.string.just_now)
        minutes < 60 -> context.getString(R.string.minutes_ago, minutes)
        minutes < 48 * 60 -> context.getString(R.string.hours_ago, minutes / 60)
        else -> context.getString(R.string.days_ago, minutes / (24 * 60))
    }
}

/** 2_734_000 → "2.7M". */
fun compactNumber(n: Long): String = when {
    n >= 1_000_000_000 -> String.format(java.util.Locale.US, "%.1fB", n / 1e9)
    n >= 1_000_000 -> String.format(java.util.Locale.US, "%.1fM", n / 1e6)
    n >= 1_000 -> String.format(java.util.Locale.US, "%.1fK", n / 1e3)
    else -> n.toString()
}
