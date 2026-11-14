package io.github.jeiel85.claudeusage.push

import io.github.jeiel85.claudeusage.net.Endpoint
import io.github.jeiel85.claudeusage.net.ProtocolException
import io.github.jeiel85.claudeusage.net.SCHEMA
import org.json.JSONArray
import org.json.JSONObject
import java.security.MessageDigest
import java.time.OffsetDateTime
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

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

/**
 * A PC's current paths, pushed silently when a new one appears (#225, ported from Homebase M-23;
 * fixture `push-endpoints.v1.json`). The app only learns new paths from `/api/v1/endpoints`, which
 * needs a working path first — so when the PC restarts and its Cloudflare Quick Tunnel gets a new
 * address, a phone that could only reach it that way would be stranded until paired again.
 */
data class PushEndpointsUpdate(val pcId: String, val tsEpochMillis: Long, val endpoints: List<Endpoint>) {
    /**
     * Whether this push should replace the stored paths, given the PC-clock time of the last push applied.
     * Push servers can deliver late or twice; an older list must not bring back dead paths. The PC's clock
     * is only compared with itself, so skew between PC and phone doesn't matter.
     */
    fun isNewerThan(lastAppliedTsMillis: Long): Boolean = endpoints.isNotEmpty() && tsEpochMillis > lastAppliedTsMillis

    companion object {
        const val KIND = "endpoints"

        /** `kind` of a decrypted push, or null for a notification (or anything that isn't JSON). */
        fun kindOf(json: String): String? = try {
            JSONObject(json).optString("kind").ifEmpty { null }
        } catch (_: Exception) {
            null
        }

        /** `pcId` of the envelope, to look up which token verifies it. */
        fun pcIdOf(json: String): String = try {
            JSONObject(json).optString("pcId")
        } catch (_: Exception) {
            ""
        }

        /**
         * Checks the MAC with [token] (this phone's bearer token for that PC) and parses the signed part.
         * Every PC shares this phone's push keys, so decrypting proves nothing about which PC sent it;
         * the MAC (key = SHA-256 of the token, which only that PC and this phone know) does. Without it
         * one PC could rewrite another PC's paths to its own address and collect that PC's token.
         * Throws [ProtocolException] for a bad MAC, a pcId mismatch or a malformed payload.
         */
        fun verify(json: String, token: String): PushEndpointsUpdate {
            val o = try {
                JSONObject(json)
            } catch (e: Exception) {
                throw ProtocolException("path push is not JSON")
            }
            if (o.optInt("schema") != SCHEMA || o.optString("kind") != KIND) throw ProtocolException("not a path push")
            val data = try {
                WebPush.unb64url(o.getString("data"))
            } catch (e: Exception) {
                throw ProtocolException("path push data is malformed")
            }
            val mac = try {
                WebPush.unb64url(o.getString("mac"))
            } catch (e: Exception) {
                throw ProtocolException("path push mac is malformed")
            }
            val key = MessageDigest.getInstance("SHA-256").digest(token.toByteArray(Charsets.UTF_8))
            val expected = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(key, "HmacSHA256")) }.doFinal(data)
            if (!MessageDigest.isEqual(expected, mac)) throw ProtocolException("path push signature mismatch")

            val inner = try {
                JSONObject(data.toString(Charsets.UTF_8))
            } catch (e: Exception) {
                throw ProtocolException("path push body is not JSON")
            }
            if (inner.optInt("schema") != SCHEMA) throw ProtocolException("unsupported path push schema ${inner.optInt("schema")}")
            val pcId = inner.optString("pcId")
            if (pcId.isEmpty() || pcId != o.optString("pcId")) throw ProtocolException("path push PC mismatch")
            val ts = try {
                OffsetDateTime.parse(inner.getString("ts")).toInstant().toEpochMilli()
            } catch (e: Exception) {
                throw ProtocolException("path push time is malformed")
            }
            val endpoints = Endpoint.parseList(inner.optJSONArray("endpoints"))
            if (endpoints.isEmpty()) throw ProtocolException("path push has no usable path")
            return PushEndpointsUpdate(pcId, ts, endpoints)
        }
    }
}
