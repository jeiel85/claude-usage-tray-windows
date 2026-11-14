package io.github.jeiel85.claudeusage.push

import android.content.Context
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONArray
import org.json.JSONObject

/**
 * One received alert kept for the notification center (#235). [atMs] is the PC's event time (what the Android
 * notification shows too); [receivedAtMs] is when this phone got it, which decides "unread" — a push delivered
 * late is still new to the user even though the event is older than the last visit.
 */
data class HistoryEntry(
    val id: String,
    val pcName: String,
    val category: String,
    val priority: String,
    val title: String,
    val body: String?,
    val clickUrl: String?,
    val atMs: Long,
    val receivedAtMs: Long,
)

/**
 * Pure list rules for the history, so they are unit-tested on the JVM: newest first, the last
 * [KEEP_MS] only, at most [MAX_ENTRIES], and an id the list already holds is not added twice
 * (a push server can deliver the same message twice).
 */
object NotificationHistoryLog {
    const val KEEP_MS = 30L * 24 * 3600 * 1000
    const val MAX_ENTRIES = 200

    fun add(entries: List<HistoryEntry>, entry: HistoryEntry, nowMs: Long): List<HistoryEntry> {
        val kept = prune(entries, nowMs)
        if (entry.id.isNotBlank() && kept.any { it.id == entry.id }) return kept
        return prune(kept + entry, nowMs)
    }

    fun prune(entries: List<HistoryEntry>, nowMs: Long): List<HistoryEntry> =
        entries.filter { nowMs - it.receivedAtMs < KEEP_MS }
            .sortedWith(compareByDescending<HistoryEntry> { it.atMs }.thenByDescending { it.receivedAtMs })
            .take(MAX_ENTRIES)

    fun unreadCount(entries: List<HistoryEntry>, lastSeenMs: Long): Int = entries.count { it.receivedAtMs > lastSeenMs }

    fun fromMessage(message: PushMessage, receivedAtMs: Long) = HistoryEntry(
        id = message.id,
        pcName = message.pcName,
        category = message.category,
        priority = message.priority,
        title = message.title,
        body = message.body,
        clickUrl = message.clickUrl,
        atMs = message.tsMs,
        receivedAtMs = receivedAtMs,
    )

    /** Unreadable or partial rows are skipped, never the whole history. */
    fun parse(json: String?): List<HistoryEntry> = try {
        val arr = JSONArray(json ?: "[]")
        (0 until arr.length()).mapNotNull { i ->
            val o = arr.optJSONObject(i) ?: return@mapNotNull null
            val title = o.optString("title")
            if (title.isBlank()) return@mapNotNull null
            HistoryEntry(
                id = o.optString("id"),
                pcName = o.optString("pc"),
                category = o.optString("cat"),
                priority = o.optString("prio", "info"),
                title = title,
                body = o.optString("body").ifBlank { null },
                clickUrl = o.optString("url").takeIf { it.startsWith("https://") },
                atMs = o.optLong("at"),
                receivedAtMs = o.optLong("rcv"),
            )
        }
    } catch (_: Exception) {
        emptyList()
    }

    fun serialize(entries: List<HistoryEntry>): String = JSONArray().apply {
        entries.forEach { e ->
            put(
                JSONObject()
                    .put("id", e.id)
                    .put("pc", e.pcName)
                    .put("cat", e.category)
                    .put("prio", e.priority)
                    .put("title", e.title)
                    .put("body", e.body ?: "")
                    .put("url", e.clickUrl ?: "")
                    .put("at", e.atMs)
                    .put("rcv", e.receivedAtMs),
            )
        }
    }.toString()
}

/**
 * The notification center's store (#235): alerts this phone received, kept only on this phone
 * (SharedPreferences, never sent anywhere). The push receiver and the screen run in the same process, so
 * one in-memory copy exposed as [StateFlow]s keeps an open notification center live without polling.
 */
object NotificationHistory {
    private const val NAME = "cut_push_history"
    private const val KEY_ENTRIES = "entries"
    private const val KEY_LAST_SEEN = "lastSeen"
    private val LOCK = Any()

    private val _entries = MutableStateFlow<List<HistoryEntry>>(emptyList())
    val entries: StateFlow<List<HistoryEntry>> = _entries.asStateFlow()

    private val _lastSeenMs = MutableStateFlow(0L)
    val lastSeenMs: StateFlow<Long> = _lastSeenMs.asStateFlow()

    private var loaded = false

    /** Reads the stored history once per process; drops what aged out while the app was closed. */
    fun load(context: Context) {
        synchronized(LOCK) {
            if (loaded) return
            val prefs = prefs(context)
            val stored = NotificationHistoryLog.parse(prefs.getString(KEY_ENTRIES, null))
            _entries.value = NotificationHistoryLog.prune(stored, System.currentTimeMillis())
            _lastSeenMs.value = prefs.getLong(KEY_LAST_SEEN, 0L)
            loaded = true
        }
    }

    /** Called by the push receiver for every alert that passed the mute and duplicate checks. */
    fun record(context: Context, message: PushMessage) {
        synchronized(LOCK) {
            load(context)
            val now = System.currentTimeMillis()
            val next = NotificationHistoryLog.add(_entries.value, NotificationHistoryLog.fromMessage(message, now), now)
            _entries.value = next
            prefs(context).edit().putString(KEY_ENTRIES, NotificationHistoryLog.serialize(next)).apply()
        }
    }

    /** Everything received so far counts as seen (the center was opened or closed). */
    fun markAllRead(context: Context) {
        synchronized(LOCK) {
            load(context)
            val seen = maxOf(System.currentTimeMillis(), _entries.value.maxOfOrNull { it.receivedAtMs } ?: 0L)
            _lastSeenMs.value = seen
            prefs(context).edit().putLong(KEY_LAST_SEEN, seen).apply()
        }
    }

    fun clear(context: Context) {
        synchronized(LOCK) {
            load(context)
            _entries.value = emptyList()
            prefs(context).edit().remove(KEY_ENTRIES).apply()
        }
    }

    private fun prefs(context: Context) = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)
}
