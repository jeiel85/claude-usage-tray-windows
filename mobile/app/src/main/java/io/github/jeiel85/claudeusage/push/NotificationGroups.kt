package io.github.jeiel85.claudeusage.push

import io.github.jeiel85.claudeusage.R

/**
 * What the user can choose to receive. Each entry is one of the PC's notification `category` values
 * (docs/MOBILE_PROTOCOL.md). The app sends the muted **categories** to every PC, so a category a newer
 * PC adds is received until the user turns it off (Homebase M-25).
 */
data class NotificationGroup(val category: String, val label: Int, val description: Int)

object NotificationGroups {
    /** The PC's "send test" — never muted, so the path can always be checked. */
    const val TEST = "test"

    val ALL: List<NotificationGroup> = listOf(
        NotificationGroup("usage", R.string.cat_usage, R.string.cat_usage_desc),
        NotificationGroup("exhaustion", R.string.cat_exhaustion, R.string.cat_exhaustion_desc),
        NotificationGroup("rate_limit", R.string.cat_rate_limit, R.string.cat_rate_limit_desc),
        NotificationGroup("reset", R.string.cat_reset, R.string.cat_reset_desc),
        NotificationGroup("weather", R.string.cat_weather, R.string.cat_weather_desc),
    )

    /** Whether a message of [category] should be dropped. [TEST] always passes. */
    fun isMuted(category: String, muted: Set<String>): Boolean = category != TEST && category in muted

    /**
     * Whether an alert should sound and vibrate (#297). The PC marks what needs the user now as `warning` or
     * `critical`: usage at 90/100%, a rate limit (work has stopped) and official weather warnings. Everything else
     * (50/75%, resets, forecasts, early-exhaustion estimates, tests) lands quietly in the shade.
     */
    fun isUrgent(priority: String): Boolean = priority == "warning" || priority == "critical"

    /** String resource for a category's label (unknown categories share "other"). */
    fun label(category: String): Int = ALL.firstOrNull { it.category == category }?.label
        ?: if (category == TEST) R.string.cat_test else R.string.cat_other
}
