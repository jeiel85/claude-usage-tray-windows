package io.github.jeiel85.claudeusage.update

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import android.text.format.Formatter
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
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
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.DialogProperties
import io.github.jeiel85.claudeusage.BuildConfig
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.ui.Palette
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import java.io.File
import java.util.concurrent.atomic.AtomicBoolean

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

    /** "지금 확인" 이 끝날 때마다 올라간다. [Banner] 가 이 값으로 판단을 다시 한다. */
    private var refreshEpoch by mutableIntStateOf(0)

    /** 프로세스 전체에서 다운로드·설치 흐름 하나만 돌게 하는 가드(같은 캐시 파일을 두 흐름이 쓰지 않게). */
    private val downloadInProgress = AtomicBoolean(false)

    private const val DOWNLOAD_DIR = "updates"
    private const val DOWNLOAD_FILE_NAME = "update.apk"

    /**
     * Input : 없음
     * Output: 새 버전이 있을 때만 눌러서 업데이트 창을 여는 한 줄 배너, 없으면 아무것도 그리지 않음
     * 핵심 로직: 확인은 배경에서, 표시는 홈에서만. 앱 시작을 모달로 막지 않는다 — 업데이트는 사용자가 지금 하려던 일이 아니다.
     *           판단은 캐시된 원문으로 네트워크 없이 재현된다([UpdateRefresh.run]). 알림을 눌러 들어왔으면 창을 바로 연다.
     */
    @Composable
    fun Banner(modifier: Modifier = Modifier) {
        val context = LocalContext.current
        val preferences = remember(context) { UpdatePreferences(context) }
        var offered by remember { mutableStateOf<UpdateManifest?>(null) }
        var dialogOpen by remember { mutableStateOf(false) }
        val openRequested = openRequest
        val epoch = refreshEpoch

        LaunchedEffect(openRequested, epoch) {
            offered = withContext(Dispatchers.IO) {
                // 지난번 설치 앱에 넘긴 APK 를 치운다. 설치 앱은 뜨자마자 파일을 복사하므로 이 화면에 돌아올 무렵엔 다 읽은 뒤다.
                // 내려받는 중이면 그 파일이라 건드리지 않는다.
                if (!downloadInProgress.get()) UpdateInstaller.deleteLeftovers(File(context.cacheDir, DOWNLOAD_DIR))
                UpdateRefresh.run(preferences)
            }
            if (offered == null) UpdateNotifier.cancel(context) // 설치·건너뛰기·끄기 뒤 남은 알림도 치운다.
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

        if (dialogOpen) {
            UpdateDialog(
                manifest = manifest,
                onDismiss = { dialogOpen = false },
                onSkip = {
                    preferences.setSkippedVersionCode(manifest.versionCode)
                    UpdateNotifier.cancel(context)
                    offered = null
                    dialogOpen = false
                },
            )
        }
    }

    /**
     * Input : 없음
     * Output: 알림 탭의 "앱 업데이트" 카드 — 켜기/끄기, 무엇을 하는지 설명, 현재 버전, 지금 확인, (권한이 없으면) 알림 허용 안내
     * 핵심 로직: 켜는 순간 무엇이 달라지는지(하루 1회 GitHub 요청, 설치는 내가 누를 때만, SHA-256 대조)를 같은 자리에서 말한다.
     */
    @Composable
    fun SettingsCard(canNotify: Boolean, onAllowNotifications: () -> Unit) {
        val context = LocalContext.current
        val scope = rememberCoroutineScope()
        val preferences = remember(context) { UpdatePreferences(context) }
        var enabled by remember { mutableStateOf(preferences.isEnabled()) }
        var checking by remember { mutableStateOf(false) }
        var result by remember { mutableStateOf<String?>(null) }

        Column(
            Modifier.fillMaxWidth().clip(RoundedCornerShape(14.dp)).background(Palette.Card).padding(16.dp),
        ) {
            Row(Modifier.fillMaxWidth(), verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text(stringResource(R.string.update_settings_title), color = Palette.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium)
                    Text(stringResource(R.string.update_current_version, BuildConfig.VERSION_NAME), color = Palette.TextDim, fontSize = 11.sp)
                }
                Switch(
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
                    colors = SwitchDefaults.colors(checkedTrackColor = Palette.Accent),
                )
            }
            Text(stringResource(R.string.update_settings_description), color = Palette.TextSoft, fontSize = 12.sp, modifier = Modifier.padding(top = 6.dp))
            if (enabled) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    TextButton(
                        enabled = !checking,
                        onClick = {
                            checking = true
                            scope.launch {
                                result = checkNow(context)
                                checking = false
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
    }

    /**
     * Input : 컨텍스트
     * Output: 한 줄 결과 문구(꺼져 있으면 null)
     * 핵심 로직: 하루 1회 규칙 없이 지금 확인하고([UpdateRefresh.runNow]), 새 버전이면 홈 배너가 뜨도록 [Banner] 를 다시 돌린다.
     */
    private suspend fun checkNow(context: Context): String? {
        val result = withContext(Dispatchers.IO) { UpdateRefresh.runNow(UpdatePreferences(context)) }
        refreshEpoch++
        return when (result) {
            is UpdateRefresh.ManualResult.Offer -> context.getString(R.string.update_result_available, result.manifest.versionName)
            UpdateRefresh.ManualResult.UpToDate -> context.getString(R.string.update_result_latest, BuildConfig.VERSION_NAME)
            UpdateRefresh.ManualResult.Failed -> context.getString(R.string.update_result_failed)
            UpdateRefresh.ManualResult.Disabled -> null
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
     * 업데이트 창의 상태 기계: 제안 → (권한 필요) → 내려받는 중 → 설치 앱으로 넘김(창 닫힘) / 실패(다시 시도·브라우저).
     * 내려받는 동안은 닫을 방법을 주지 않는다 — 블로킹 다운로드는 창을 닫아도 멈추지 않아, "닫았는데 잠시 뒤 설치 화면이 뜨는"
     * 혼란과 재시도가 같은 파일을 다시 쓰는 경합을 만든다.
     */
    @Composable
    private fun UpdateDialog(manifest: UpdateManifest, onDismiss: () -> Unit, onSkip: () -> Unit) {
        val context = LocalContext.current
        val size = remember(manifest) { Formatter.formatShortFileSize(context, manifest.apkSizeBytes) }
        var flow by remember(manifest) { mutableStateOf(FlowState.Offer) }
        // LaunchedEffect 재실행 열쇠: 0 은 "아직 안 함", 시도할 때마다 하나씩 올린다("다시 시도" 도 새 값이어야 다시 돈다).
        var downloadEpoch by remember(manifest) { mutableIntStateOf(0) }

        fun startDownload() {
            if (!downloadInProgress.compareAndSet(false, true)) return
            // 매번 다시 묻는다 — 사용자가 설정에서 권한을 바꿨을 수 있다.
            flow = if (context.packageManager.canRequestPackageInstalls()) {
                downloadEpoch++
                FlowState.Downloading
            } else {
                downloadInProgress.set(false)
                FlowState.NeedsInstallPermission
            }
        }

        // 설정 화면은 결과 코드로 승인 여부를 알려 주지 않는다 — 돌아온 뒤 PackageManager 에 다시 묻는다.
        val installPermission = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { startDownload() }

        LaunchedEffect(downloadEpoch) {
            if (downloadEpoch == 0) return@LaunchedEffect
            try {
                val destination = File(File(context.cacheDir, DOWNLOAD_DIR).apply { mkdirs() }, DOWNLOAD_FILE_NAME)
                val downloaded = withContext(Dispatchers.IO) {
                    UpdateDownloader().download(manifest.apkUrl, destination, manifest.sha256)
                }
                val installed = when (downloaded) {
                    is UpdateDownloader.Result.Success -> UpdateInstaller.install(context, downloaded.file)
                    UpdateDownloader.Result.Failed -> false
                }
                if (installed) onDismiss() else flow = FlowState.Failed
            } finally {
                downloadInProgress.set(false)
            }
        }

        val canDismiss = flow != FlowState.Downloading
        AlertDialog(
            onDismissRequest = onDismiss,
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
                            onDismiss()
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
                if (canDismiss) TextButton(onClick = onDismiss) { Text(stringResource(R.string.update_dialog_close)) }
            },
        )
    }

    /** 링크는 OS 에 넘긴다. 실패해도 조용히 넘어간다. */
    private fun Context.open(url: String) {
        runCatching { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
    }
}
