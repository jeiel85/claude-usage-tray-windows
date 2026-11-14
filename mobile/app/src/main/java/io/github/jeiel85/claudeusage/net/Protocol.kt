package io.github.jeiel85.claudeusage.net

import org.json.JSONArray
import org.json.JSONObject
import java.time.OffsetDateTime
import java.time.format.DateTimeParseException

/**
 * Wire models mirroring the PC's `Services/Mobile/MobileProtocol.cs` (docs/MOBILE_PROTOCOL.md).
 * Parsing uses the platform's org.json — no serialization library in the APK. Parsers return
 * null (or throw [ProtocolException]) instead of half-filled objects.
 */
const val SCHEMA = 1

class ProtocolException(message: String) : Exception(message)

/** Pairing QR content. `cu: 1` marks it as this app's code (a Homebase QR carries `hb` instead). */
data class PairingOffer(
    val pcId: String,
    val pcName: String,
    val endpoints: List<Endpoint>,
    val code: String,
) {
    companion object {
        /** QR text (pairing link or raw JSON) → offer, or null when it isn't this app's code. */
        fun parse(text: String): PairingOffer? = try {
            val o = JSONObject(PairingLink.decode(text) ?: text.trim())
            if (o.optInt("cu") != 1) {
                null
            } else {
                PairingOffer(
                    pcId = o.getString("pcId"),
                    pcName = o.getString("pcName"),
                    endpoints = Endpoint.parseList(o.optJSONArray("endpoints")),
                    code = o.getString("code"),
                ).takeIf { it.endpoints.isNotEmpty() && it.code.isNotBlank() && it.pcId.isNotBlank() }
            }
        } catch (_: Exception) {
            null
        }
    }
}

data class PairResponse(val deviceId: String, val token: String, val pcId: String, val pcName: String) {
    companion object {
        fun parse(json: String): PairResponse {
            val o = JSONObject(json)
            requireSchema(o)
            return PairResponse(o.getString("deviceId"), o.getString("token"), o.getString("pcId"), o.getString("pcName"))
        }
    }
}

/** `GET /api/v1/endpoints` → the PC's current paths. */
fun parseEndpointsResponse(json: String): List<Endpoint> {
    val o = JSONObject(json)
    requireSchema(o)
    return Endpoint.parseList(o.optJSONArray("endpoints"))
}

/** Which window a gauge row is: the provider's short / long window, or a named extra limit. */
enum class GaugeKind { SHORT, LONG, EXTRA, NAMED }

/**
 * One usage bar. [usage] is a fraction 0..1 (the PC sends fractions). [name] is the server's own
 * name for NAMED rows (model limits, Antigravity groups, OpenCode windows) and empty otherwise —
 * the UI labels SHORT/LONG by [windowMinutes] ("5h", "7d") so no provider wording is translated here.
 */
data class Gauge(
    val kind: GaugeKind,
    val name: String,
    val usage: Double,
    val resetAtMs: Long?,
    val windowMinutes: Int?,
) {
    /**
     * The value to show at [nowMs]. A window whose reset time has passed has been refilled on the
     * provider's side even if no PC observed it yet — show 0 instead of yesterday's number.
     */
    fun effective(nowMs: Long): Gauge =
        if (resetAtMs != null && resetAtMs <= nowMs) copy(usage = 0.0, resetAtMs = null) else this
}

/** Account-level quota (same meaning as the PC's `UsageSyncQuotaSnapshot`). */
data class Quota(
    val hasData: Boolean,
    val observedAtMs: Long?,
    val planType: String,
    val gauges: List<Gauge>,
    val extraCreditsLabel: String,
) {
    companion object {
        private const val CLAUDE_SHORT_MINUTES = 300
        private const val CLAUDE_LONG_MINUTES = 7 * 24 * 60

        fun parse(o: JSONObject?, provider: String): Quota? {
            if (o == null) return null
            val gauges = mutableListOf<Gauge>()
            val shortReset = time(o, "shortResetAt")
            val longReset = time(o, "longResetAt")
            val shortMinutes = optInt(o, "shortWindowMinutes") ?: if (provider == "claude") CLAUDE_SHORT_MINUTES else null
            val longMinutes = optInt(o, "longWindowMinutes") ?: if (provider == "claude") CLAUDE_LONG_MINUTES else null
            // Claude always has a 5-hour window when it has data; other providers name theirs by reset time.
            val hasShort = shortReset != null || (provider == "claude" && o.optBoolean("hasData"))
            val hasLong = o.optBoolean("hasLongWindow") || longReset != null

            val models = o.optJSONArray("models").objects()
            val openCode = o.optJSONObject("openCode")
            when {
                openCode != null -> {
                    for (key in listOf("rolling", "weekly", "monthly")) {
                        val w = openCode.optJSONObject(key) ?: continue
                        gauges += Gauge(GaugeKind.NAMED, key, fraction(w, "usagePercent"), time(w, "resetAt"), null)
                    }
                }
                models.isNotEmpty() -> {
                    for (m in models) {
                        val name = listOf(m.optString("displayName"), m.optString("groupName"), m.optString("modelId"))
                            .firstOrNull { it.isNotBlank() } ?: continue
                        val window = m.optString("window").takeIf { it.isNotBlank() }
                        val label = if (window != null) "$name · $window" else name
                        gauges += Gauge(GaugeKind.NAMED, label, 1.0 - fraction(m, "remainingFraction"), time(m, "resetAt"), null)
                    }
                }
                else -> {
                    if (hasShort) gauges += Gauge(GaugeKind.SHORT, "", fraction(o, "shortUsagePercent"), shortReset, shortMinutes)
                    if (hasLong) gauges += Gauge(GaugeKind.LONG, "", fraction(o, "longUsagePercent"), longReset, longMinutes)
                }
            }
            for (a in o.optJSONArray("additionalQuotas").objects()) {
                val name = a.optString("limitName").ifBlank { a.optString("limitId") }
                if (name.isBlank()) continue
                gauges += Gauge(GaugeKind.NAMED, name, fraction(a, "usagePercent"), time(a, "resetAt"), optInt(a, "windowMinutes"))
            }
            if (o.optBoolean("extraHasLimit") && !o.isNull("extraUsagePercent")) {
                gauges += Gauge(GaugeKind.EXTRA, "", fraction(o, "extraUsagePercent"), null, null)
            }
            return Quota(
                hasData = o.optBoolean("hasData"),
                observedAtMs = time(o, "observedAtUtc"),
                planType = o.optString("planType"),
                gauges = gauges,
                extraCreditsLabel = o.optString("extraCreditsLabel"),
            )
        }
    }
}

/**
 * Tokens used today on one PC (device-level; summed across PCs on an account card). [date] is the PC's
 * local date (yyyy-MM-dd): a PC that was off overnight still has yesterday's totals cached, and they
 * must not be added to today's (Codex review P2).
 */
data class TodayTotals(
    val date: String,
    val inputTokens: Long,
    val outputTokens: Long,
    val cacheReadTokens: Long,
    val cacheWriteTokens: Long,
    val sessionCount: Int,
    val requestCount: Int,
) {
    val totalTokens: Long get() = inputTokens + outputTokens + cacheReadTokens + cacheWriteTokens

    operator fun plus(o: TodayTotals) = TodayTotals(
        maxOf(date, o.date),
        inputTokens + o.inputTokens, outputTokens + o.outputTokens, cacheReadTokens + o.cacheReadTokens,
        cacheWriteTokens + o.cacheWriteTokens, sessionCount + o.sessionCount, requestCount + o.requestCount,
    )

    companion object {
        fun parse(o: JSONObject?): TodayTotals? = o?.let {
            TodayTotals(
                it.optString("date"),
                it.optLong("inputTokens"), it.optLong("outputTokens"), it.optLong("cacheReadTokens"),
                it.optLong("cacheWriteTokens"), it.optInt("sessionCount"), it.optInt("requestCount"),
            )
        }
    }
}

/** One provider's usage on one PC. [accountScope] "device" means the PC couldn't identify the account. */
data class ProviderUsage(
    val provider: String,
    val accountHash: String,
    val accountScope: String,
    val accountLabel: String,
    val observedAtMs: Long,
    val errorKind: String,
    val quota: Quota?,
    val today: TodayTotals?,
) {
    val isAccountScoped: Boolean get() = accountScope == SCOPE_ACCOUNT

    companion object {
        const val SCOPE_ACCOUNT = "account"
        const val SCOPE_DEVICE = "device"

        fun parse(o: JSONObject): ProviderUsage? {
            val provider = o.optString("provider").lowercase()
            val hash = o.optString("accountHash")
            if (provider.isBlank() || !hash.matches(HASH)) return null
            return ProviderUsage(
                provider = provider,
                accountHash = hash,
                accountScope = if (o.optString("accountScope") == SCOPE_ACCOUNT) SCOPE_ACCOUNT else SCOPE_DEVICE,
                accountLabel = o.optString("accountLabel"),
                observedAtMs = time(o, "observedAt") ?: 0L,
                errorKind = o.optString("errorKind"),
                quota = Quota.parse(o.optJSONObject("quota"), provider),
                today = TodayTotals.parse(o.optJSONObject("today")),
            )
        }

        private val HASH = Regex("^[0-9a-f]{64}$")
    }
}

/** `GET /api/v1/usage` and every `WS /api/v1/live` frame. */
data class UsageSnapshot(
    val pcId: String,
    val pcName: String,
    val appVersion: String,
    val tsMs: Long,
    val providers: List<ProviderUsage>,
) {
    companion object {
        fun parse(json: String): UsageSnapshot {
            val o = JSONObject(json)
            requireSchema(o)
            return UsageSnapshot(
                pcId = o.getString("pcId"),
                pcName = o.optString("pcName"),
                appVersion = o.optString("appVersion"),
                tsMs = time(o, "ts") ?: 0L,
                providers = o.optJSONArray("providers").objects().mapNotNull(ProviderUsage::parse),
            )
        }
    }
}

/** The PC's error body (`{schema, error, message}`), for showing why pairing failed. */
fun errorCode(json: String?): String? = try {
    json?.let { JSONObject(it).optString("error").ifBlank { null } }
} catch (_: Exception) {
    null
}

private fun requireSchema(o: JSONObject) {
    val schema = o.optInt("schema")
    if (schema != SCHEMA) throw ProtocolException("Unsupported schema $schema")
}

/** ISO 8601 with offset (what .NET writes for DateTimeOffset) → epoch ms, or null. */
internal fun time(o: JSONObject, key: String): Long? {
    if (o.isNull(key)) return null
    val text = o.optString(key).ifBlank { return null }
    return try {
        OffsetDateTime.parse(text).toInstant().toEpochMilli()
    } catch (_: DateTimeParseException) {
        null
    }
}

private fun optInt(o: JSONObject, key: String): Int? = if (o.has(key) && !o.isNull(key)) o.optInt(key) else null

private fun fraction(o: JSONObject, key: String): Double = o.optDouble(key, 0.0).let { if (it.isNaN()) 0.0 else it.coerceIn(0.0, 1.0) }

internal fun JSONArray?.objects(): List<JSONObject> =
    if (this == null) emptyList() else (0 until length()).mapNotNull { optJSONObject(it) }
