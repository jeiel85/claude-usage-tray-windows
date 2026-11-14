package io.github.jeiel85.claudeusage.ui

import android.content.Context
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.data.PcEntry
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.net.PairException
import io.github.jeiel85.claudeusage.net.PairingOffer
import io.github.jeiel85.claudeusage.net.UsageApi
import kotlinx.coroutines.launch

/**
 * QR pairing (Homebase's flow): camera scan is the main path; pasting the link is the fallback.
 * A link that opened the app is only filled in — the user taps Connect, because any web page can
 * send such a link and pairing must be a deliberate act.
 */
@Composable
fun PairScreen(store: PcStore, api: UsageApi, onDone: (PcEntry) -> Unit, onBack: () -> Unit, link: String? = null) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var pasted by remember { mutableStateOf("") }

    fun handle(text: String?) {
        if (text.isNullOrBlank()) return
        val offer = PairingOffer.parse(text)
        if (offer == null) {
            message = context.getString(R.string.pair_not_ours)
            return
        }
        busy = true
        message = context.getString(R.string.pair_connecting, offer.pcName)
        scope.launch {
            try {
                val (res, endpoint) = api.pair(offer, deviceName(context), "${Build.MANUFACTURER} ${Build.MODEL}")
                val pc = PcEntry(res.pcId, res.pcName, offer.endpoints, res.deviceId, res.token, endpoint.key)
                store.upsert(pc)
                onDone(pc)
            } catch (e: PairException) {
                message = context.getString(
                    when (e.code) {
                        "code_expired" -> R.string.pair_err_expired
                        "locked" -> R.string.pair_err_locked
                        else -> R.string.pair_err_invalid
                    },
                )
            } catch (e: Exception) {
                message = context.getString(R.string.pair_err_unreachable, e.message ?: "")
            } finally {
                busy = false
            }
        }
    }

    LaunchedEffect(link) {
        if (link == null) return@LaunchedEffect
        pasted = link
        message = PairingOffer.parse(link)?.let { context.getString(R.string.pair_link_opened, it.pcName) }
            ?: context.getString(R.string.pair_not_ours)
    }

    val scanner = rememberLauncherForActivityResult(ScanContract()) { result -> handle(result.contents) }
    val prompt = stringResource(R.string.pair_scan_prompt)

    Column(
        Modifier
            .fillMaxSize()
            .background(Palette.Bg)
            .statusBarsPadding()
            .navigationBarsPadding()
            .imePadding()
            .verticalScroll(rememberScrollState())
            .padding(horizontal = 20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(Modifier.fillMaxWidth().padding(top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onBack) { Text("← " + stringResource(R.string.back), color = Palette.AccentLight) }
            Spacer(Modifier.weight(1f))
        }
        Text(stringResource(R.string.pair_title), color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
        Text("1. " + stringResource(R.string.pair_step1), color = Palette.TextSoft, fontSize = 13.sp)
        Text("2. " + stringResource(R.string.pair_step2), color = Palette.TextSoft, fontSize = 13.sp)

        Button(
            onClick = {
                scanner.launch(
                    ScanOptions()
                        .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                        .setPrompt(prompt)
                        .setBeepEnabled(false)
                        .setOrientationLocked(true),
                )
            },
            enabled = !busy,
            modifier = Modifier.fillMaxWidth().height(52.dp),
        ) { Text(stringResource(R.string.pair_scan), fontSize = 16.sp) }

        OutlinedTextField(
            value = pasted,
            onValueChange = { pasted = it },
            label = { Text(stringResource(R.string.pair_paste_hint)) },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        OutlinedButton(onClick = { handle(pasted) }, enabled = !busy && pasted.isNotBlank(), modifier = Modifier.fillMaxWidth()) {
            Text(stringResource(R.string.pair_connect))
        }

        if (busy) CircularProgressIndicator(Modifier.size(28.dp).align(Alignment.CenterHorizontally))
        message?.let { Text(it, color = Palette.Text, fontSize = 13.sp) }
        Spacer(Modifier.height(24.dp))
    }
}

/** What the PC lists under "Paired phones": the user's own device name when Android has one. */
private fun deviceName(context: Context): String =
    Settings.Global.getString(context.contentResolver, Settings.Global.DEVICE_NAME)?.takeIf { it.isNotBlank() }
        ?: "${Build.MANUFACTURER} ${Build.MODEL}"
