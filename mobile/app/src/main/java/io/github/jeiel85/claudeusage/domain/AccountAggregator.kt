package io.github.jeiel85.claudeusage.domain

import io.github.jeiel85.claudeusage.net.Gauge
import io.github.jeiel85.claudeusage.net.ProviderUsage
import io.github.jeiel85.claudeusage.net.Quota
import io.github.jeiel85.claudeusage.net.TodayTotals
import io.github.jeiel85.claudeusage.net.UsageSnapshot

/** What the app knows about one paired PC right now. [snapshot] is the last one received (maybe cached). */
data class PcView(
    val pcId: String,
    val pcName: String,
    val online: Boolean,
    val snapshot: UsageSnapshot?,
    val receivedAtMs: Long?,
)

/** One PC's contribution to an account card. */
data class AccountDevice(
    val pcId: String,
    val pcName: String,
    val online: Boolean,
    val observedAtMs: Long,
    val today: TodayTotals?,
    val errorKind: String,
)

/**
 * One account as the user thinks of it: every PC that reported the same provider + account hash.
 *
 * [quota] is account-level (the provider's server says the same thing to every PC), so it is not
 * summed — the freshest observation wins, and [quotaFromPcName] says which PC it came from.
 * [today] is device-level (tokens spent on each PC), so it IS summed across the PCs.
 */
data class AccountCard(
    val key: String,
    val provider: String,
    val accountScoped: Boolean,
    val label: String,
    val planType: String,
    val quota: Quota?,
    val gauges: List<Gauge>,
    val quotaObservedAtMs: Long?,
    val quotaFromPcName: String?,
    val devices: List<AccountDevice>,
    val today: TodayTotals?,
)

object AccountAggregator {

    private val PROVIDER_ORDER = listOf("claude", "codex", "opencode", "antigravity")

    /**
     * Input : every paired PC with its latest snapshot, and the current time
     * Output: account cards, Claude → Codex → OpenCode → Antigravity, then by label
     * Key logic:
     * - Group by (provider, accountHash). The PC only sends a SHA-256 of the account id, so two PCs on
     *   one account collide on purpose and two accounts never do. When a PC couldn't identify the
     *   account it mixes its own pcId into the hash ("device" scope), so such PCs stay separate
     *   instead of being lumped together as one unknown account.
     * - Quota: newest observation that actually has data. A PC whose API call failed (rate limit,
     *   offline) still sends an entry without data; it must not blank out a good value from another PC.
     * - Gauges are evaluated at [nowMs]: a window whose reset time passed shows 0 (see [Gauge.effective]).
     */
    fun aggregate(pcs: List<PcView>, nowMs: Long): List<AccountCard> {
        data class Seen(val pc: PcView, val usage: ProviderUsage)

        val groups = LinkedHashMap<String, MutableList<Seen>>()
        for (pc in pcs) {
            val snapshot = pc.snapshot ?: continue
            for (usage in snapshot.providers) {
                groups.getOrPut("${usage.provider}|${usage.accountHash}") { mutableListOf() } += Seen(pc, usage)
            }
        }

        return groups.map { (key, seen) ->
            val provider = seen.first().usage.provider
            val withQuota = seen
                .filter { it.usage.quota?.hasData == true }
                .maxByOrNull { it.usage.quota?.observedAtMs ?: it.usage.observedAtMs }
            val newest = seen.maxBy { it.usage.observedAtMs }
            val label = seen.sortedByDescending { it.usage.observedAtMs }
                .map { it.usage.accountLabel }.firstOrNull { it.isNotBlank() } ?: ""
            val quota = withQuota?.usage?.quota
            val devices = seen
                .sortedWith(compareBy<Seen> { it.pc.pcName.lowercase() }.thenBy { it.pc.pcId })
                .map {
                    AccountDevice(it.pc.pcId, it.pc.pcName, it.pc.online, it.usage.observedAtMs, it.usage.today, it.usage.errorKind)
                }
            val today = devices.mapNotNull { it.today }.reduceOrNull { a, b -> a + b }
            AccountCard(
                key = key,
                provider = provider,
                accountScoped = newest.usage.isAccountScoped,
                label = label,
                planType = quota?.planType?.takeIf { it.isNotBlank() }
                    ?: seen.mapNotNull { it.usage.quota?.planType?.takeIf(String::isNotBlank) }.firstOrNull() ?: "",
                quota = quota,
                gauges = quota?.gauges?.map { it.effective(nowMs) } ?: emptyList(),
                quotaObservedAtMs = withQuota?.let { it.usage.quota?.observedAtMs ?: it.usage.observedAtMs },
                quotaFromPcName = withQuota?.pc?.pcName,
                devices = devices,
                today = today,
            )
        }.sortedWith(
            compareBy<AccountCard> { PROVIDER_ORDER.indexOf(it.provider).let { i -> if (i < 0) Int.MAX_VALUE else i } }
                .thenBy { it.provider }
                .thenBy { if (it.accountScoped) 0 else 1 }
                .thenBy { it.label.lowercase() }
                .thenBy { it.key },
        )
    }

    /** The bar the summary chip shows: the most used window of the card (what will run out first). */
    fun headline(card: AccountCard): Gauge? = card.gauges.maxByOrNull { it.usage }
}
