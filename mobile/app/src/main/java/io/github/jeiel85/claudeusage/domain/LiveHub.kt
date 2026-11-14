package io.github.jeiel85.claudeusage.domain

import io.github.jeiel85.claudeusage.data.PcEntry
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.net.LiveState
import io.github.jeiel85.claudeusage.net.UsageApi
import io.github.jeiel85.claudeusage.net.UsageSnapshot
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.collectLatest
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.map
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

enum class PcConnection { CONNECTING, LIVE, RETRYING, UNAUTHORIZED, INCOMPATIBLE, OFFLINE }

/** One PC as the screens see it: connection state plus the last snapshot (live or cached). */
data class PcStatus(
    val pc: PcEntry,
    val connection: PcConnection,
    val endpointLabel: String?,
    val detail: String?,
    val retryInSeconds: Int?,
    val snapshot: UsageSnapshot?,
    val receivedAtMs: Long?,
) {
    fun toView() = PcView(pc.pcId, pc.name, connection == PcConnection.LIVE, snapshot, receivedAtMs)
}

/**
 * Keeps one live WebSocket per paired PC while [run] is active (the activity is STARTED) — the account
 * screen needs every PC at once to merge accounts. When the app goes to the background the sockets
 * close, like Homebase (M-13): the PC only re-sends what it already polls, so nothing runs for nothing.
 */
class LiveHub(
    private val store: PcStore,
    private val api: UsageApi,
    private val clock: () -> Long = System::currentTimeMillis,
    /** Called on the first frame of every (re)connection — the moment to (re)register push with that PC. */
    private val onConnected: (PcEntry) -> Unit = {},
) {
    private val _status = MutableStateFlow<Map<String, PcStatus>>(emptyMap())
    val status: StateFlow<Map<String, PcStatus>> = _status.asStateFlow()

    init {
        seed(store.all())
    }

    /**
     * Input : none (follows [PcStore.pcs])
     * Output: never returns normally; cancel it to close every socket
     * Key logic: restarts the per-PC connections only when the SET of PCs (id + token) changes — a
     * remembered path or refreshed endpoint list also changes the store, and must not drop and redial
     * every socket. On cancel every PC is marked OFFLINE, keeping its last snapshot on screen.
     */
    suspend fun run() {
        try {
            store.pcs
                .map { list -> list.map { it.pcId to it.token } }
                .distinctUntilChanged()
                .collectLatest { ids ->
                    val wanted = ids.map { it.first }.toSet()
                    _status.update { m -> m.filterKeys { it in wanted } }
                    seed(store.all())
                    coroutineScope {
                        for ((pcId, _) in ids) {
                            val pc = store.get(pcId) ?: continue
                            launch { follow(pc) }
                        }
                    }
                }
        } finally {
            _status.update { m -> m.mapValues { (_, s) -> s.copy(connection = PcConnection.OFFLINE, retryInSeconds = null) } }
        }
    }

    private suspend fun follow(pc: PcEntry) {
        api.live(
            pc,
            onGoodEndpoint = { store.rememberGood(pc.pcId, it) },
            onEndpoints = { store.updateEndpoints(pc.pcId, it) },
            latest = { store.get(pc.pcId) },
        ).collect { state -> apply(pc, state) }
    }

    private fun apply(pc: PcEntry, state: LiveState) {
        val now = clock()
        if (state is LiveState.Live && _status.value[pc.pcId]?.connection != PcConnection.LIVE) {
            onConnected(store.get(pc.pcId) ?: pc)
        }
        if (state is LiveState.Live) store.cacheSnapshot(pc.pcId, state.json, now)
        _status.update { m ->
            val old = m[pc.pcId] ?: PcStatus(pc, PcConnection.CONNECTING, null, null, null, null, null)
            val next = when (state) {
                LiveState.Connecting -> old.copy(connection = PcConnection.CONNECTING, retryInSeconds = null)
                is LiveState.Live -> old.copy(
                    connection = PcConnection.LIVE,
                    endpointLabel = state.endpoint.label,
                    detail = null,
                    retryInSeconds = null,
                    snapshot = state.snapshot,
                    receivedAtMs = now,
                )
                is LiveState.Retrying -> old.copy(connection = PcConnection.RETRYING, detail = state.reason, retryInSeconds = state.inSeconds)
                LiveState.Unauthorized -> old.copy(connection = PcConnection.UNAUTHORIZED, retryInSeconds = null)
                is LiveState.Incompatible -> old.copy(connection = PcConnection.INCOMPATIBLE, detail = state.reason, retryInSeconds = null)
            }
            m + (pc.pcId to next.copy(pc = store.get(pc.pcId) ?: pc))
        }
    }

    /** Adds PCs not yet in the map, with their cached snapshot so the screen isn't empty before connecting. */
    private fun seed(pcs: List<PcEntry>) {
        _status.update { m ->
            val added = pcs.filter { it.pcId !in m }.associate { pc ->
                val cached = store.cachedSnapshot(pc.pcId)
                val snapshot = cached?.let { runCatching { UsageSnapshot.parse(it.first) }.getOrNull() }
                pc.pcId to PcStatus(pc, PcConnection.OFFLINE, null, null, null, snapshot, cached?.second?.takeIf { snapshot != null })
            }
            m + added
        }
    }
}
