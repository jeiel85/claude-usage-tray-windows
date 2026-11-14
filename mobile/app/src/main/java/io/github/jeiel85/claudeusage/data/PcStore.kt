package io.github.jeiel85.claudeusage.data

import android.content.Context
import io.github.jeiel85.claudeusage.net.Endpoint
import io.github.jeiel85.claudeusage.net.LocalNetworks
import io.github.jeiel85.claudeusage.net.RouteSelector
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import org.json.JSONArray
import org.json.JSONObject

/** A paired PC and every path to it (from the QR, refreshed from the PC after each connect). */
data class PcEntry(
    val pcId: String,
    val name: String,
    val endpoints: List<Endpoint>,
    val deviceId: String,
    val token: String,
    /** [Endpoint.key] of the path that worked last; preferred among equally good paths. */
    val lastGood: String? = null,
) {
    /** Paths to try now, best first, for the phone's current network. */
    fun orderedEndpoints(net: LocalNetworks?): List<Endpoint> = RouteSelector.order(endpoints, net, lastGood)
}

/**
 * Paired PCs in SharedPreferences (ported from Homebase). The bearer token is encrypted with
 * [KeystoreCipher], so a backup or file copy doesn't carry usable credentials. The last snapshot
 * of each PC is cached separately (plain JSON — usage numbers, no secrets) so the account screen
 * shows something right away and while a PC is offline.
 */
class PcStore(context: Context) {

    private val prefs = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)
    private val cache = context.applicationContext.getSharedPreferences(CACHE_NAME, Context.MODE_PRIVATE)
    private val cipher = KeystoreCipher(KEY_ALIAS)
    private val _pcs = MutableStateFlow(load())

    /** Every change (pair, remove, new path) is published here; the live hub watches it. */
    val pcs: StateFlow<List<PcEntry>> = _pcs.asStateFlow()

    fun all(): List<PcEntry> = _pcs.value

    fun get(pcId: String): PcEntry? = all().firstOrNull { it.pcId == pcId }

    /** Adds or replaces (re-pairing the same PC keeps one entry). */
    @Synchronized
    fun upsert(pc: PcEntry) = save(all().filterNot { it.pcId == pc.pcId } + pc)

    @Synchronized
    fun remove(pcId: String) {
        save(all().filterNot { it.pcId == pcId })
        cache.edit().remove(pcId).remove("$pcId.at").apply()
    }

    @Synchronized
    fun rememberGood(pcId: String, endpoint: Endpoint) {
        val list = all()
        if (list.none { it.pcId == pcId && it.lastGood != endpoint.key }) return
        save(list.map { if (it.pcId == pcId) it.copy(lastGood = endpoint.key) else it })
    }

    /** The PC's current paths (DHCP change, Tailscale turned on). */
    @Synchronized
    fun updateEndpoints(pcId: String, endpoints: List<Endpoint>) {
        if (endpoints.isEmpty()) return
        val list = all()
        if (list.none { it.pcId == pcId && it.endpoints != endpoints }) return
        save(list.map { if (it.pcId == pcId) it.copy(endpoints = endpoints) else it })
    }

    fun cacheSnapshot(pcId: String, json: String, receivedAtMs: Long) {
        cache.edit().putString(pcId, json).putLong("$pcId.at", receivedAtMs).apply()
    }

    /** (snapshot JSON, received time) last cached for [pcId], or null. */
    fun cachedSnapshot(pcId: String): Pair<String, Long>? {
        val json = cache.getString(pcId, null) ?: return null
        return json to cache.getLong("$pcId.at", 0L)
    }

    private fun load(): List<PcEntry> {
        val raw = prefs.getString(KEY_PCS, null) ?: return emptyList()
        return try {
            val arr = JSONArray(raw)
            (0 until arr.length()).mapNotNull { i -> arr.optJSONObject(i)?.let(::fromJson) }
        } catch (_: Exception) {
            emptyList()
        }
    }

    private fun save(list: List<PcEntry>) {
        val arr = JSONArray()
        list.forEach { arr.put(toJson(it)) }
        prefs.edit().putString(KEY_PCS, arr.toString()).apply()
        _pcs.value = list
    }

    private fun toJson(pc: PcEntry) = JSONObject()
        .put("pcId", pc.pcId)
        .put("name", pc.name)
        .put("endpoints", JSONArray().apply { pc.endpoints.forEach { put(it.toJson()) } })
        .put("deviceId", pc.deviceId)
        .put("token", cipher.encrypt(pc.token.toByteArray(Charsets.UTF_8)))
        .put("lastGood", pc.lastGood)

    /** null when the Keystore key is gone (e.g. app data restored elsewhere) — the PC must be re-paired. */
    private fun fromJson(o: JSONObject): PcEntry? {
        val token = cipher.decrypt(o.optString("token"))?.toString(Charsets.UTF_8) ?: return null
        return PcEntry(
            pcId = o.optString("pcId"),
            name = o.optString("name"),
            endpoints = Endpoint.parseList(o.optJSONArray("endpoints")),
            deviceId = o.optString("deviceId"),
            token = token,
            lastGood = o.optString("lastGood").ifBlank { null },
        )
    }

    private companion object {
        const val NAME = "cut_pcs"
        const val CACHE_NAME = "cut_snapshots"
        const val KEY_PCS = "pcs"
        const val KEY_ALIAS = "cut_pc_tokens"
    }
}
