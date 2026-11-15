package io.github.jeiel85.claudeusage.update

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.text.format.Formatter
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.DialogProperties
import io.github.jeiel85.claudeusage.BuildConfig
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.ui.AppSwitch
import io.github.jeiel85.claudeusage.ui.CardShape
import io.github.jeiel85.claudeusage.ui.Palette
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/**
 * 앱 화면에 붙는 업데이트 UI(Homebase `UpdateSurface` 이식, #228). 홈 화면은 [Banner] 만, 알림 탭은 [SettingsCard] 만 부르면 된다 —
 * 업데이트 기능이 어떻게 확인·다운로드·설치하는지는 몰라도 된다.
 */
internal object UpdateSurface {

    /**
     * 업데이트 알림을 눌러 앱이 열렸다는 표시. MainActivity 가 켜고 [Banner] 가 소비한다. 프로세스 전역인 이유: 알림은 액티비티가
     * 이미 떠 있을 때(onNewIntent)도 눌릴 수 있고, 그때 배너는 이미 화면에 있다 — 값이 바뀌면 배너가 판단을 다시 하고 창을 연다.
     */
    private var openRequest by mutableStateOf(false)

    fun requestOpen() {
        openRequest = true
    }

    /** "지금 확인"·켜기/끄기 뒤에 올라간다. 홈 배너와 설정 카드가 이 값으로 판단을 다시 한다. */
    private var refreshEpoch by mutableIntStateOf(0)

    /**
     * 지금 설치를 권할 새 버전(없으면 null). 홈 배너와 설정 카드가 함께 본다(#247) — 한쪽에서 확인·건너뛰기·설치한 결과가 다른 쪽에도
     * 그대로 보여, 설정에서 확인한 업데이트를 홈으로 나가 배너에서 다시 찾지 않아도 된다.
     */
    private var offered by mutableStateOf<UpdateManifest?>(null)

    /**
     * Input : 컨텍스트, [UpdatePreferences]
     * Output: 없음([offered] 갱신)
     * 핵심 로직: 판단은 캐시된 원문으로 재현되고 네트워크는 하루 1회 규칙을 따른다([UpdateRefresh.run]). 지난번 설치 앱에 넘긴 APK 를
     *           치운다 — 설치 앱은 뜨자마자 파일을 복사하므로 화면에 돌아올 무렵엔 다 읽은 뒤다. 내려받는 중이면 그 파일이라 건드리지
     *           않는다. 권할 버전이 없으면 설치·건너뛰기·끄기 뒤 남은 알림도 치운다.
     */
    private suspend fun refreshOffer(context: Context, preferences: UpdatePreferences) {
        offered = withContext(Dispatchers.IO) {
            if (!UpdateDownloads.holdsFile) UpdateInstaller.deleteLeftovers(UpdateDownloads.directory(context))
            UpdateRefresh.run(preferences)
        }
        if (offered == null) UpdateNotifier.cancel(context)
    }

    /** 업데이트 창. 배너와 설정 카드가 같은 창·같은 건너뛰기 처리를 쓴다. */
    @Composable
    private fun OfferDialog(manifest: UpdateManifest, preferences: UpdatePreferences, onClose: () -> Unit) {
        val context = LocalContext.current
        UpdateDialog(
            manifest = manifest,
            onDismiss = onClose,
            onSkip = {
                preferences.setSkippedVersionCode(manifest.versionCode)
                UpdateNotifier.cancel(context)
                offered = null
                onClose()
            },
        )
    }

    /**
     * Input : 없음
     * Output: 새 버전이 있을 때만 눌러서 업데이트 창을 여는 한 줄 배너, 없으면 아무것도 그리지 않음
     * 핵심 로직: 확인은 배경에서. 앱 시작을 모달로 막지 않는다 — 업데이트는 사용자가 지금 하려던 일이 아니다. 알림을 눌러
     *           들어왔으면 창을 바로 연다.
     */
    @Composable
    fun Banner(modifier: Modifier = Modifier) {
        val context = LocalContext.current
        val preferences = remember(context) { UpdatePreferences(context) }
        // 화면을 돌려 액티비티가 다시 만들어져도 창이 다시 뜨게 저장한다 — 다운로드는 UpdateDownloads 가 이어서 들고 있다.
        var dialogOpen by rememberSaveable { mutableStateOf(false) }
        val openRequested = openRequest
        val epoch = refreshEpoch

        LaunchedEffect(openRequested, epoch) {
            refreshOffer(context, preferences)
            if (openRequested) {
                if (offered != null) dialogOpen = true
                openRequest = false
            }
        }

        val manifest = offered ?: return
        Text(
            text = stringResource(R.string.update_banner, manifest.versionName),
            color = Palette.Text,
            fontSize = 13.sp,
            fontWeight = FontWeight.Medium,
            modifier = modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(12.dp))
                .background(Palette.Accent.copy(alpha = 0.25f))
                .clickable { dialogOpen = true }
                .padding(horizontal = 16.dp, vertical = 12.dp),
        )

        if (dialogOpen) OfferDialog(manifest, preferences, onClose = { dialogOpen = false })
    }

    /**
     * Input : 없음
     * Output: 설정의 "앱 업데이트" 카드 — 켜기/끄기, 무엇을 하는지 설명, 현재 버전, (새 버전이 있으면) 바로 설치 버튼, 지금 확인,
     *         (권한이 없으면) 알림 허용 안내
     * 핵심 로직: 켜는 순간 무엇이 달라지는지(하루 1회 GitHub 요청, 설치는 내가 누를 때만, SHA-256 대조)를 같은 자리에서 말한다.
     *           새 버전은 홈 배너와 같은 창으로 **이 카드에서 바로** 설치한다(#247) — "지금 확인" 으로 찾았으면 창을 곧바로 연다.
     */
    @Composable
    fun SettingsCard(canNotify: Boolean, onAllowNotifications: () -> Unit) {
        val context = LocalContext.current
        val scope = rememberCoroutineScope()
        val preferences = remember(context) { UpdatePreferences(context) }
        var enabled by remember { mutableStateOf(preferences.isEnabled()) }
        var checking by remember { mutableStateOf(false) }
        var result by remember { mutableStateOf<String?>(null) }
        var dialogOpen by rememberSaveable { mutableStateOf(false) }
        val epoch = refreshEpoch

        // 홈을 거치지 않고 설정부터 열었어도(알림 → 앱 → 설정 등) 이미 받아 둔 새 버전을 보여 준다.
        LaunchedEffect(epoch) { refreshOffer(context, preferences) }

        Column(
            Modifier.fillMaxWidth().clip(CardShape).background(Palette.Card).border(1.dp, Palette.CardBorder, CardShape).padding(16.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(stringResource(R.string.update_settings_title), color = Palette.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium)
                    Text(stringResource(R.string.update_current_version, BuildConfig.VERSION_NAME), color = Palette.TextDim, fontSize = 11.sp)
                }
                AppSwitch(
                    checked = enabled,
                    onCheckedChange = { checked ->
                        preferences.setEnabled(checked)
                        enabled = checked
                        result = null
                        UpdateCheckScheduler.sync(context)
                        // 배너가 이 설정을 바로 따르게 한다: 끄면 떠 있던 배너가 사라지고(설치 흐름도 막힘), 켜면 캐시·하루 1회
                        // 규칙대로 다시 판단한다. 배너는 refreshEpoch 가 바뀔 때만 다시 판단하므로 여기서 올린다(Codex 리뷰).
                        refreshEpoch++
                    },
                )
            }
            Text(stringResource(R.string.update_settings_description), color = Palette.TextSoft, fontSize = 12.sp, modifier = Modifier.padding(top = 6.dp))
            val manifest = offered
            if (enabled && manifest != null) {
                Row(
                    Modifier
                        .padding(top = 12.dp)
                        .fillMaxWidth()
                        .clip(RoundedCornerShape(12.dp))
                        .background(Palette.Accent.copy(alpha = 0.18f))
                        .padding(start = 14.dp, end = 6.dp, top = 6.dp, bottom = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        stringResource(R.string.update_result_available, manifest.versionName),
                        color = Palette.Text,
                        fontSize = 13.sp,
                        fontWeight = FontWeight.Medium,
                        modifier = Modifier.weight(1f),
                    )
                    Button(onClick = { dialogOpen = true }) { Text(stringResource(R.string.update_dialog_update)) }
                }
            }
            if (enabled) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    TextButton(
                        enabled = !checking,
                        onClick = {
                            checking = true
                            scope.launch {
                                val outcome = checkNow(context)
                                result = outcome.message
                                checking = false
                                if (outcome.offer) dialogOpen = true
                            }
                        },
                    ) { Text(stringResource(R.string.update_check_now), color = Palette.AccentLight) }
                    if (checking) CircularProgressIndicator(Modifier.size(16.dp), strokeWidth = 2.dp, color = Palette.AccentLight)
                }
                result?.let { Text(it, color = Palette.TextSoft, fontSize = 12.sp) }
                if (!canNotify) {
                    TextButton(onClick = onAllowNotifications) {
                        Text(stringResource(R.string.update_notifications_off), color = Palette.Warn, fontSize = 12.sp)
                    }
                }
            }
        }

        // 건너뛰기·끄기로 권할 버전이 사라지면 열려 있던 창 표시도 접는다(나중에 새 버전이 생겼을 때 저절로 뜨지 않게).
        val current = offered
        LaunchedEffect(current) { if (current == null) dialogOpen = false }
        if (dialogOpen && enabled && current != null) OfferDialog(current, preferences, onClose = { dialogOpen = false })
    }

    /** "지금 확인" 의 결과: 카드에 남길 한 줄(새 버전이면 카드의 설치 줄이 대신 말하므로 null)과 창을 열지 여부. */
    private class CheckOutcome(val message: String?, val offer: Boolean)

    /**
     * Input : 컨텍스트
     * Output: [CheckOutcome]
     * 핵심 로직: 하루 1회 규칙 없이 지금 확인하고([UpdateRefresh.runNow]), 결과를 [offered] 에 바로 반영한다 — 설정 카드와 홈 배너가
     *           같은 값을 보므로 어느 쪽에서든 이어서 설치할 수 있다.
     */
    private suspend fun checkNow(context: Context): CheckOutcome {
        val preferences = UpdatePreferences(context)
        val result = withContext(Dispatchers.IO) { UpdateRefresh.runNow(preferences) }
        // 확인하는 동안 업데이트를 껐으면 늦게 온 결과로 권할 버전을 되살리지 않는다(Codex 리뷰) — 끄기가 이미 비운 상태를 덮어
        // 홈 배너와 창이 꺼진 설정을 거스르게 된다.
        if (!preferences.isEnabled()) return CheckOutcome(null, offer = false)
        return when (result) {
            is UpdateRefresh.ManualResult.Offer -> {
                offered = result.manifest
                CheckOutcome(null, offer = true)
            }
            UpdateRefresh.ManualResult.UpToDate -> {
                refreshEpoch++
                CheckOutcome(context.getString(R.string.update_result_latest, BuildConfig.VERSION_NAME), offer = false)
            }
            UpdateRefresh.ManualResult.Failed -> CheckOutcome(context.getString(R.string.update_result_failed), offer = false)
            UpdateRefresh.ManualResult.Disabled -> CheckOutcome(null, offer = false)
        }
    }

    private enum class FlowState { Offer, NeedsInstallPermission, Downloading, Failed }

    /**
     * 이 설치본이 CI 의 디버그 서명 APK 인가(서명 키 시크릿이 없던 릴리스, v1.53·v1.54). 디버그 키는 빌드마다 달라서, 정식 서명
     * 업데이트를 받아도 Android 가 "앱이 설치되지 않음" 으로 거부한다 — 내려받고 설치 화면까지 간 뒤에야 실패하는 것보다, 처음부터
     * "한 번 지우고 다시 설치" 를 안내한다. 로컬 http 서버로 흐름을 검증하는 디버그 빌드(UPDATE_ALLOW_HTTP)는 같은 키로 서명한
     * 테스트 APK 를 쓰므로 원래 흐름을 그대로 탄다.
     */
    private val needsReinstall: Boolean get() = BuildConfig.DEBUG && !BuildConfig.UPDATE_ALLOW_HTTP

    /**
     * 업데이트 창: 제안 → (권한 필요) → 내려받는 중 → 설치 앱으로 넘김(창 닫힘) / 실패(다시 시도·브라우저).
     * 다운로드 자체는 [UpdateDownloads] 가 프로세스 전체에서 들고 있고, 이 창은 그 상태를 보여 줄 뿐이다 — 화면을 돌려 창이 다시
     * 만들어져도 진행이 끊기지 않고, 끝난 파일은 다시 만들어진 창이 설치 화면으로 넘긴다.
     * 내려받는 동안은 닫을 방법을 주지 않는다 — 닫아도 다운로드는 멈추지 않아 "닫았는데 잠시 뒤 설치 화면이 뜨는" 혼란을 만든다.
     */
    @Composable
    private fun UpdateDialog(manifest: UpdateManifest, onDismiss: () -> Unit, onSkip: () -> Unit) {
        val context = LocalContext.current
        val lifecycle = LocalLifecycleOwner.current.lifecycle
        val size = remember(manifest) { Formatter.formatShortFileSize(context, manifest.apkSizeBytes) }
        val download by UpdateDownloads.state.collectAsState()
        var needsPermission by rememberSaveable(manifest.versionCode) { mutableStateOf(false) }
        val mine = { vc: Int -> vc == manifest.versionCode }
        val flow = when (val d = download) {
            is UpdateDownloads.State.Running -> if (mine(d.versionCode)) FlowState.Downloading else FlowState.Offer
            is UpdateDownloads.State.Ready -> if (mine(d.versionCode)) FlowState.Downloading else FlowState.Offer
            is UpdateDownloads.State.Failed -> if (mine(d.versionCode)) FlowState.Failed else FlowState.Offer
            UpdateDownloads.State.Idle -> if (needsPermission) FlowState.NeedsInstallPermission else FlowState.Offer
        }

        fun startDownload() {
            // 매번 다시 묻는다 — 사용자가 설정에서 권한을 바꿨을 수 있다.
            if (context.packageManager.canRequestPackageInstalls()) {
                needsPermission = false
                UpdateDownloads.reset() // 지난 실패를 지우고
                UpdateDownloads.start(context, manifest)
            } else {
                needsPermission = true
            }
        }

        // 설정 화면은 결과 코드로 승인 여부를 알려 주지 않는다 — 돌아온 뒤 PackageManager 에 다시 묻는다.
        val installPermission = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { startDownload() }

        // 받은 파일은 이 창이 화면에 보일 때(STARTED) 설치 화면으로 넘긴다 — 백그라운드에서 다른 앱 화면을 띄우는 것은 Android 가
        // 막으므로, 사용자가 돌아오면 그때 넘긴다.
        LaunchedEffect(download) {
            val ready = download as? UpdateDownloads.State.Ready ?: return@LaunchedEffect
            if (!mine(ready.versionCode)) return@LaunchedEffect
            lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
                if (UpdateInstaller.install(context, ready.file)) {
                    UpdateDownloads.reset()
                    onDismiss()
                } else {
                    UpdateDownloads.fail(ready.versionCode)
                }
            }
        }

        // 실패를 보고 닫으면 다음에 열 때 처음부터 고를 수 있게 상태를 지운다.
        val dismiss = {
            if (flow == FlowState.Failed) UpdateDownloads.reset()
            onDismiss()
        }
        val canDismiss = flow != FlowState.Downloading
        AlertDialog(
            onDismissRequest = dismiss,
            properties = DialogProperties(dismissOnBackPress = canDismiss, dismissOnClickOutside = canDismiss),
            title = { Text(stringResource(R.string.update_dialog_title)) },
            text = {
                when (flow) {
                    FlowState.Offer -> Column {
                        Text(stringResource(R.string.update_dialog_offer, manifest.versionName, size))
                        if (needsReinstall) {
                            Text(stringResource(R.string.update_dialog_reinstall), color = Palette.Warn, fontSize = 12.sp,
                                modifier = Modifier.padding(top = 8.dp))
                        }
                        // 변경 내용은 manifest 의 링크로 — 지금 깔린 앱은 새 버전의 변경 내용을 모른다.
                        TextButton(onClick = { context.open(manifest.releaseNotesUrl) }) { Text(stringResource(R.string.update_dialog_notes)) }
                        TextButton(onClick = onSkip) { Text(stringResource(R.string.update_dialog_skip)) }
                    }
                    FlowState.NeedsInstallPermission -> Text(stringResource(R.string.update_dialog_permission))
                    FlowState.Downloading -> Row(verticalAlignment = Alignment.CenterVertically) {
                        CircularProgressIndicator(Modifier.size(24.dp))
                        Spacer(Modifier.width(12.dp))
                        Text(stringResource(R.string.update_dialog_downloading))
                    }
                    FlowState.Failed -> Column {
                        Text(stringResource(R.string.update_dialog_failed))
                        // 최종 수단: 앱이 직접 받는 길이 막혀도 막다른 길이 아니게 브라우저에 맡긴다.
                        TextButton(onClick = {
                            context.open(manifest.apkUrl)
                            dismiss()
                        }) { Text(stringResource(R.string.update_dialog_browser)) }
                    }
                }
            },
            confirmButton = {
                when (flow) {
                    FlowState.Offer -> if (needsReinstall) {
                        TextButton(onClick = {
                            context.open(manifest.releaseNotesUrl)
                            onDismiss()
                        }) { Text(stringResource(R.string.update_dialog_release_page)) }
                    } else {
                        TextButton(onClick = ::startDownload) { Text(stringResource(R.string.update_dialog_update)) }
                    }
                    FlowState.NeedsInstallPermission -> TextButton(onClick = {
                        // ACTION_MANAGE_UNKNOWN_APP_SOURCES 는 API 26(= minSdk) 부터 — 버전 분기가 필요 없다.
                        installPermission.launch(
                            Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}")),
                        )
                    }) { Text(stringResource(R.string.update_dialog_open_settings)) }
                    FlowState.Downloading -> Unit
                    FlowState.Failed -> TextButton(onClick = ::startDownload) { Text(stringResource(R.string.update_dialog_retry)) }
                }
            },
            dismissButton = {
                if (canDismiss) TextButton(onClick = dismiss) { Text(stringResource(R.string.update_dialog_close)) }
            },
        )
    }

    /** 링크는 OS 에 넘긴다. 실패해도 조용히 넘어간다. */
    private fun Context.open(url: String) {
        runCatching { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }
}
