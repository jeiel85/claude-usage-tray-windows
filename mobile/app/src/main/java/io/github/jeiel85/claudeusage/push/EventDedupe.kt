package io.github.jeiel85.claudeusage.push

import org.json.JSONArray
import org.json.JSONObject

/**
 * Shows an alert once even when several PCs send it (#222). This is the phone side of the PC's
 * `NtfyEventKey` (#175), which used ntfy's sequence_id: keys look like `prefix` or `prefix_epoch`,
 * where epoch is the event time floored to the minute. Two keys are the same event when the prefixes
 * match and the epochs are within the sender's tolerance (plus the 59 s lost to flooring).
 *
 * Pure logic with a JSON round-trip, so it is unit-tested on the JVM.
 */
object EventDedupe {
    /** Remember alerts for a day — the longest a PC keeps an alert relevant (push TTL). */
    const val KEEP_MS = 24 * 3600 * 1000L
    private const val MAX_ENTRIES = 200
    private const val FLOOR_SECONDS = 60L

    data class Seen(val key: String, val atMs: Long)

    private fun split(key: String): Pair<String, Long?> {
        val i = key.lastIndexOf('_')
        if (i <= 0) return key to null
        val epoch = key.substring(i + 1).toLongOrNull() ?: return key to null
        return key.substring(0, i) to epoch
    }

    /** Whether [key] (with [toleranceSec]) matches one of [seen]. */
    fun isDuplicate(key: String, toleranceSec: Int, seen: List<Seen>): Boolean {
        val (prefix, epoch) = split(key)
        return seen.any { s ->
            val (p, e) = split(s.key)
            when {
                p != prefix -> false
                epoch == null || e == null -> epoch == null && e == null
                else -> kotlin.math.abs(epoch - e) <= toleranceSec + FLOOR_SECONDS - 1
            }
        }
    }

    /**
     * Input : stored JSON, the new key (or null for keyless messages such as tests), tolerance, now
     * Output: (show it?, JSON to store)
     * Keyless messages always show and are not remembered (a test must arrive every time it's pressed).
     */
    fun check(stored: String?, key: String?, toleranceSec: Int, nowMs: Long): Pair<Boolean, String?> {
        val seen = parse(stored).filter { nowMs - it.atMs < KEEP_MS }
        if (key == null) return true to stored
        if (isDuplicate(key, toleranceSec, seen)) return false to serialize(seen)
        return true to serialize((seen + Seen(key, nowMs)).takeLast(MAX_ENTRIES))
    }

    fun parse(json: String?): List<Seen> = try {
        val arr = JSONArray(json ?: "[]")
        (0 until arr.length()).mapNotNull { i ->
            arr.optJSONObject(i)?.let { o -> o.optString("k").takeIf { it.isNotBlank() }?.let { Seen(it, o.optLong("t")) } }
        }
    } catch (_: Exception) {
        emptyList()
    }

    fun serialize(seen: List<Seen>): String =
        JSONArray().apply { seen.forEach { put(JSONObject().put("k", it.key).put("t", it.atMs)) } }.toString()
}
