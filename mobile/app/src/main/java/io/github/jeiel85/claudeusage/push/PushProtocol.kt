package io.github.jeiel85.claudeusage.push

import io.github.jeiel85.claudeusage.net.ProtocolException
import io.github.jeiel85.claudeusage.net.SCHEMA
import org.json.JSONArray
import org.json.JSONObject
import java.time.OffsetDateTime

/**
 * Push wire models mirroring the PC's `Services/Mobile/MobilePush.cs` (docs/MOBILE_PROTOCOL.md,
 * fixtures `push-subscription.v1.json`, `push-message.v1.json`).
 */

/** What this phone registers with each PC: its UnifiedPush endpoint and RFC 8291 keys (base64url). */
data class PushSubscription(
    val endpoint: String,
    val p256dh: String,
    val auth: String,
    /** Categories this phone does not want; the PC skips them. Sorted so equal choices compare equal. */
    val mutedCategories: List<String> = emptyList(),
) {
    fun toJson(): JSONObject = JSONObject()
        .put("schema", SCHEMA)
        .put("endpoint", endpoint)
        .put("p256dh", p256dh)
        .put("auth", auth)
        .put("mutedCategories", JSONArray(mutedCategories.sorted()))
}

data class PushSubscriptionResponse(val deviceId: String, val registered: Boolean) {
    companion object {
        fun parse(json: String): PushSubscriptionResponse {
            val o = JSONObject(json)
            if (o.optInt("schema") != SCHEMA) throw ProtocolException("Unsupported schema ${o.optInt("schema")}")
            return PushSubscriptionResponse(o.getString("deviceId"), o.getBoolean("registered"))
        }
    }
}

/**
 * One decrypted notification. [eventKey] + [eventToleranceSec] identify the event across PCs (the PC's
 * `NtfyEventKey`): several PCs watching one account send the same alert and [EventDedupe] shows it once.
 */
data class PushMessage(
    val id: String,
    val pcId: String,
    val pcName: String,
    val category: String,
    val priority: String,
    val title: String,
    val body: String?,
    val eventKey: String?,
    val eventToleranceSec: Int,
    val clickUrl: String?,
    val tsMs: Long,
) {
    companion object {
        /** Throws [ProtocolException] for another schema or a missing field — never a half-filled message. */
        fun parse(json: String): PushMessage {
            val o = try {
                JSONObject(json)
            } catch (e: Exception) {
                throw ProtocolException("push payload is not JSON")
            }
            if (o.optInt("schema") != SCHEMA) throw ProtocolException("Unsupported schema ${o.optInt("schema")}")
            val title = o.optString("title")
            val pcId = o.optString("pcId")
            if (title.isBlank() || pcId.isBlank()) throw ProtocolException("push payload misses title or pcId")
            return PushMessage(
                id = o.optString("id"),
                pcId = pcId,
                pcName = o.optString("pcName"),
                category = o.optString("category"),
                priority = o.optString("priority", "info"),
                title = title,
                body = o.optString("body").ifBlank { null },
                eventKey = o.optString("eventKey").ifBlank { null },
                eventToleranceSec = o.optInt("eventToleranceSec", 0).coerceAtLeast(0),
                clickUrl = o.optString("clickUrl").takeIf { it.startsWith("https://") },
                tsMs = runCatching { OffsetDateTime.parse(o.getString("ts")).toInstant().toEpochMilli() }
                    .getOrDefault(System.currentTimeMillis()),
            )
        }
    }
}
