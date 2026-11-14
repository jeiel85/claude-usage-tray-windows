package io.github.jeiel85.claudeusage.data

import android.content.Context
import io.github.jeiel85.claudeusage.net.Endpoint
import io.github.jeiel85.claudeusage.net.LocalNetworks
import io.github.jeiel85.claudeusage.net.RouteSelector
import io.github.jeiel85.claudeusage.push.PushEndpointsUpdate
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
    /** PC-clock time of the last path push applied (#225) — a late duplicate must not bring back old paths. */
    val endpointsPushTs: Long = 0L,
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

    /**
     * One list per process, shared by every PcStore instance. The activity's store feeds the live hub,
     * while a push receiver builds its own store to apply a path update (#225); with a list per instance
     * the hub would never see the new paths and keep retrying the old Quick Tunnel address.
     */
    private val _pcs: MutableStateFlow<List<PcEntry>> =
        shared ?: synchronized(LOCK) { shared ?: MutableStateFlow(load()).also { shared = it } }

    /** Every change (pair, remove, new path) is published here; the live hub watches it. */
    val pcs: StateFlow<List<PcEntry>> = _pcs.asStateFlow()

    fun all(): List<PcEntry> = _pcs.value

    fun get(pcId: String): PcEntry? = all().firstOrNull { it.pcId == pcId }

    // Writers lock the process-wide LOCK, not the instance: two stores (activity, push receiver) edit one list.

    /** Adds or replaces (re-pairing the same PC keeps one entry). */
    fun upsert(pc: PcEntry) {
        synchronized(LOCK) { save(all().filterNot { it.pcId == pc.pcId } + pc) }
    }

    fun remove(pcId: String) {
        synchronized(LOCK) {
            save(all().filterNot { it.pcId == pcId })
            cache.edit().remove(pcId).remove("$pcId.at").apply()
        }
    }

    fun rememberGood(pcId: String, endpoint: Endpoint) {
        synchronized(LOCK) {
            val list = all()
            if (list.none { it.pcId == pcId && it.lastGood != endpoint.key }) return
            save(list.map { if (it.pcId == pcId) it.copy(lastGood = endpoint.key) else it })
        }
    }

    /** The PC's current paths (DHCP change, Tailscale turned on). */
    fun updateEndpoints(pcId: String, endpoints: List<Endpoint>) {
        if (endpoints.isEmpty()) return
        synchronized(LOCK) {
            val list = all()
            if (list.none { it.pcId == pcId && it.endpoints != endpoints }) return
            save(list.map { if (it.pcId == pcId) it.copy(endpoints = endpoints) else it })
        }
    }

    /**
     * Paths the PC pushed (#225), already verified. Returns false when the PC isn't paired or the push is
     * not newer than the last one applied ([PushEndpointsUpdate.isNewerThan]).
     */
    fun applyPushedEndpoints(update: PushEndpointsUpdate): Boolean {
        synchronized(LOCK) {
            val list = all()
            val pc = list.firstOrNull { it.pcId == update.pcId } ?: return false
            if (!update.isNewerThan(pc.endpointsPushTs)) return false
            save(list.map { if (it.pcId == update.pcId) it.copy(endpoints = update.endpoints, endpointsPushTs = update.tsEpochMillis) else it })
            return true
        }
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
        .put("endpointsPushTs", pc.endpointsPushTs)

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
            endpointsPushTs = o.optLong("endpointsPushTs", 0L),
        )
    }

    private companion object {
        private val LOCK = Any()

        @Volatile
        private var shared: MutableStateFlow<List<PcEntry>>? = null

        private const val NAME = "cut_pcs"
        private const val CACHE_NAME = "cut_snapshots"
        private const val KEY_PCS = "pcs"
        private const val KEY_ALIAS = "cut_pc_tokens"
    }
}
