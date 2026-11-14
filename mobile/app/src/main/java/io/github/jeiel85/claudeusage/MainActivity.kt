package io.github.jeiel85.claudeusage

import android.content.Intent
import android.os.Bundle
import android.view.WindowManager
import androidx.activity.ComponentActivity
import androidx.activity.SystemBarStyle
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.toArgb
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.lifecycleScope
import androidx.lifecycle.repeatOnLifecycle
import io.github.jeiel85.claudeusage.data.AppPrefs
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.domain.LiveHub
import io.github.jeiel85.claudeusage.net.NetworkProbe
import io.github.jeiel85.claudeusage.net.PairingLink
import io.github.jeiel85.claudeusage.net.UsageApi
import io.github.jeiel85.claudeusage.push.PushRegistrar
import io.github.jeiel85.claudeusage.push.PushState
import io.github.jeiel85.claudeusage.ui.PushAutoSetup
import io.github.jeiel85.claudeusage.ui.AppTheme
import io.github.jeiel85.claudeusage.ui.HomeScreen
import io.github.jeiel85.claudeusage.ui.PairScreen
import io.github.jeiel85.claudeusage.ui.SettingsScreen
import io.github.jeiel85.claudeusage.update.UpdateCheckScheduler
import io.github.jeiel85.claudeusage.update.UpdateNotifier
import io.github.jeiel85.claudeusage.update.UpdateSurface
import io.github.jeiel85.claudeusage.ui.Palette
import kotlinx.coroutines.launch

/** Two screens — home (accounts / PCs) and pairing — so plain state instead of a navigation library. */
class MainActivity : ComponentActivity() {
    /** A pairing link the app was opened with (camera → link, or the install page's button). */
    private var pairingLink by mutableStateOf<String?>(null)

    override fun onCreate(savedInstanceState: Bundle?) {
        enableEdgeToEdge(
            statusBarStyle = SystemBarStyle.dark(Palette.Bg.toArgb()),
            navigationBarStyle = SystemBarStyle.dark(Palette.Bg.toArgb()),
        )
        super.onCreate(savedInstanceState)
        AppPrefs.load(this)
        pairingLink = if (savedInstanceState == null) intent.pairingLink() else savedInstanceState.getString(KEY_PAIRING_LINK)
        // Opened from the "new version" notification (#228): the home banner opens the update dialog right away.
        // Not on recreation (rotation) — the request was already consumed then.
        if (savedInstanceState == null && intent.opensUpdate()) UpdateSurface.requestOpen()
        // Keep the background update check scheduled (or cancelled) to match the user's choice.
        UpdateCheckScheduler.sync(this)

        val store = PcStore(this)
        val api = UsageApi(networks = NetworkProbe(this)::current)
        // Every (re)connection re-registers this phone's push endpoint with that PC (#222): pairing by QR is
        // all it takes, and a PC that lost the subscription (404/410, reinstall) gets it back on next contact —
        // hence force: the process-local "already sent" cache can't know the PC dropped it.
        val hub = LiveHub(store, api, onConnected = { pc -> PushRegistrar.ensureAsync(this, api, pc, force = true) })

        // Sockets only while the app is on screen: the PC would otherwise push to a phone nobody looks at.
        lifecycleScope.launch {
            repeatOnLifecycle(Lifecycle.State.STARTED) { hub.run() }
        }

        setContent {
            AppTheme {
                val status by hub.status.collectAsStateWithLifecycle()
                var pairing by rememberSaveable { mutableStateOf(false) }
                var settings by rememberSaveable { mutableStateOf(false) }
                // Settings → Display → keep the screen on while the app is visible (#230). The window flag only
                // applies while this activity is in front, so it never keeps the phone awake in the background.
                val keepOn = AppPrefs.keepScreenOn
                LaunchedEffect(keepOn) {
                    if (keepOn) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                    else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
                }
                BackHandler(enabled = settings && !pairing) { settings = false }
                LaunchedEffect(pairingLink) { if (pairingLink != null) pairing = true }
                BackHandler(enabled = pairing) {
                    pairing = false
                    pairingLink = null
                }
                if (!pairing) PushAutoSetup(store)
                if (pairing) {
                    PairScreen(
                        store, api,
                        onDone = { pc ->
                            PushRegistrar.ensureAsync(this, api, pc)
                            pairingLink = null
                            pairing = false
                        },
                        onBack = {
                            pairingLink = null
                            pairing = false
                        },
                        link = pairingLink,
                    )
                } else if (settings) {
                    SettingsScreen(store, api, onBack = { settings = false })
                } else {
                    HomeScreen(
                        status,
                        onAdd = { pairing = true },
                        onRemove = { pcId -> removePc(store, api, pcId) },
                        onSettings = { settings = true },
                    )
                }
            }
        }
    }

    /** Removes a PC and, best effort, its copy of this phone's push subscription (so it stops sending). */
    private fun removePc(store: PcStore, api: UsageApi, pcId: String) {
        val pc = store.get(pcId)
        store.remove(pcId)
        PushRegistrar.forget(pcId)
        PushState(this).markRegistered(pcId, false)
        if (pc != null) PushRegistrar.scope.launch { runCatching { api.unregisterPush(pc) } }
    }

    override fun onSaveInstanceState(outState: Bundle) {
        super.onSaveInstanceState(outState)
        pairingLink?.let { outState.putString(KEY_PAIRING_LINK, it) }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        intent.pairingLink()?.let { pairingLink = it }
        if (intent.opensUpdate()) UpdateSurface.requestOpen()
    }

    private fun Intent?.opensUpdate(): Boolean = this?.getBooleanExtra(UpdateNotifier.EXTRA_OPEN_UPDATE, false) == true

    private fun Intent?.pairingLink(): String? =
        this?.takeIf { it.action == Intent.ACTION_VIEW }?.dataString?.takeIf(PairingLink::isLink)

    private companion object {
        const val KEY_PAIRING_LINK = "pairingLink"
    }
}
