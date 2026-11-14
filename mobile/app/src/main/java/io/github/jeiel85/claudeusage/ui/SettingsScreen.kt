package io.github.jeiel85.claudeusage.ui

import android.Manifest
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.automirrored.filled.KeyboardArrowRight
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import io.github.jeiel85.claudeusage.BuildConfig
import io.github.jeiel85.claudeusage.R
import io.github.jeiel85.claudeusage.data.AppPrefs
import io.github.jeiel85.claudeusage.data.PcStore
import io.github.jeiel85.claudeusage.net.UsageApi
import io.github.jeiel85.claudeusage.push.PushNotifier
import io.github.jeiel85.claudeusage.update.UpdateSurface

/** Links the About section opens (#230). */
internal object Links {
    const val REPO = "https://github.com/jeiel85/claude-usage-tray-windows"
    const val LATEST_RELEASE = "$REPO/releases/latest"

    fun releaseNotes(versionName: String) = "$REPO/releases/tag/v$versionName"

    /**
     * Input : 앱 버전, Android 버전
     * Output: 본문에 두 버전이 채워진 새 이슈 주소
     * 핵심 로직: 신고에 가장 먼저 필요한 두 가지만 미리 채운다. 기기 모델·계정·PC 이름처럼 사용자를 알아볼 수 있는 값은
     *           넣지 않는다 — 공개 저장소의 이슈라 누구나 본다. 사용자가 보내기 전에 화면에서 고칠 수 있다.
     */
    fun newIssue(versionName: String, androidRelease: String, sdk: Int): String {
        val body = "\n\n---\nApp: $versionName\nAndroid: $androidRelease (API $sdk)\n"
        return "$REPO/issues/new?body=" + Uri.encode(body)
    }
}

/**
 * The phone app's settings (#230), opened from the gear above the tabs — the same layout as the Homebase app
 * (notifications, display, updates, about) so one habit works in both.
 *
 * Notifications moved here from their own tab: they are set once, not looked at like the account cards, and a
 * settings screen is where people look for them ("설정 자체가 안 보인다").
 */
@Composable
fun SettingsScreen(store: PcStore, api: UsageApi, onBack: () -> Unit) {
    val context = LocalContext.current
    var showLicenses by remember { mutableStateOf(false) }

    Column(Modifier.fillMaxSize().background(Palette.Bg).statusBarsPadding()) {
        Row(Modifier.fillMaxWidth().padding(start = 4.dp, end = 16.dp, top = 8.dp, bottom = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = onBack) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = stringResource(R.string.back), tint = Palette.Text)
            }
            Text(stringResource(R.string.settings_title), color = Palette.Text, fontSize = 20.sp, fontWeight = FontWeight.SemiBold)
        }
        LazyColumn(
            Modifier.fillMaxSize().navigationBarsPadding(),
            contentPadding = androidx.compose.foundation.layout.PaddingValues(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            item { SectionLabel(stringResource(R.string.settings_section_notifications)) }
            item { NotificationSettings(store, api) }

            item { SectionLabel(stringResource(R.string.settings_section_display)) }
            item { DisplaySettings() }

            item { SectionLabel(stringResource(R.string.settings_section_updates)) }
            item { UpdateCard() }

            item { SectionLabel(stringResource(R.string.settings_section_about), trailing = "v${BuildConfig.VERSION_NAME}") }
            item {
                Card {
                    Column {
                        LinkRow(stringResource(R.string.about_release_notes), stringResource(R.string.about_release_notes_desc, BuildConfig.VERSION_NAME)) {
                            context.openUrl(Links.releaseNotes(BuildConfig.VERSION_NAME))
                        }
                        Divider()
                        LinkRow(stringResource(R.string.about_pc_app), stringResource(R.string.about_pc_app_desc)) {
                            context.openUrl(Links.LATEST_RELEASE)
                        }
                        Divider()
                        LinkRow(stringResource(R.string.about_repo), stringResource(R.string.about_repo_desc)) {
                            context.openUrl(Links.REPO)
                        }
                        Divider()
                        LinkRow(stringResource(R.string.about_report), stringResource(R.string.about_report_desc)) {
                            context.openUrl(Links.newIssue(BuildConfig.VERSION_NAME, Build.VERSION.RELEASE, Build.VERSION.SDK_INT))
                        }
                        Divider()
                        LinkRow(stringResource(R.string.about_licenses), stringResource(R.string.about_licenses_desc)) { showLicenses = true }
                    }
                }
            }
            item {
                Text(stringResource(R.string.about_privacy), color = Palette.TextDim, fontSize = 11.sp, modifier = Modifier.padding(horizontal = 4.dp))
                Spacer(Modifier.height(24.dp))
            }
        }
    }

    if (showLicenses) LicensesDialog(onDismiss = { showLicenses = false })
}

@Composable
private fun SectionLabel(text: String, trailing: String? = null) {
    Row(Modifier.fillMaxWidth().padding(start = 4.dp, end = 4.dp, top = 8.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text, color = Palette.TextSoft, fontSize = 12.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
        trailing?.let { Text(it, color = Palette.TextDim, fontSize = 12.sp) }
    }
}

/**
 * Input : 없음([AppPrefs])
 * Output: 화면 카드 — 초기화 시각 표시 방식, 보는 동안 화면 켜 두기, (Android 13+) 앱 언어
 * 핵심 로직: 바꾸는 즉시 [AppPrefs] 의 컴포즈 상태가 바뀌어 홈 화면·창 플래그가 따라온다. 언어는 Android 13 부터 시스템이
 *           앱별로 관리하므로 앱 안에 따로 두지 않고 그 화면을 연다(앱 안 언어 설정과 시스템 설정이 서로 어긋나지 않게).
 */
@Composable
private fun DisplaySettings() {
    val context = LocalContext.current
    Card {
        Column(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            SwitchRow(
                title = stringResource(R.string.settings_reset_absolute),
                subtitle = stringResource(R.string.settings_reset_absolute_desc),
                checked = AppPrefs.absoluteResetTime,
                enabled = true,
                onChange = { AppPrefs.setAbsoluteResetTime(context, it) },
            )
            SwitchRow(
                title = stringResource(R.string.settings_keep_screen_on),
                subtitle = stringResource(R.string.settings_keep_screen_on_desc),
                checked = AppPrefs.keepScreenOn,
                enabled = true,
                onChange = { AppPrefs.setKeepScreenOn(context, it) },
            )
            if (Build.VERSION.SDK_INT >= 33) {
                Divider()
                LinkRow(stringResource(R.string.settings_language), stringResource(R.string.settings_language_desc)) {
                    runCatching {
                        context.startActivity(
                            Intent(Settings.ACTION_APP_LOCALE_SETTINGS, Uri.fromParts("package", context.packageName, null)),
                        )
                    }
                }
            }
        }
    }
}

/**
 * 앱 업데이트 카드(#228). 알림 권한이 없으면 확인은 돌아도 알림이 뜨지 않으므로, 그 사실과 고칠 길을 카드 안에서 준다 —
 * 처음엔 권한을 묻고, 이미 거부돼 다시 물을 수 없으면 앱 알림 설정을 연다.
 */
@Composable
private fun UpdateCard() {
    val context = LocalContext.current
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    var canNotify by remember { mutableStateOf(PushNotifier.canNotify(context)) }
    var asked by remember { mutableStateOf(false) }
    val permission = rememberLauncherForActivityResult(ActivityResultContracts.RequestPermission()) {
        canNotify = PushNotifier.canNotify(context)
    }
    // 시스템 알림 설정에서 돌아오면 다시 읽는다.
    LaunchedEffect(Unit) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) { canNotify = PushNotifier.canNotify(context) }
    }
    UpdateSurface.SettingsCard(
        canNotify = canNotify,
        onAllowNotifications = {
            if (Build.VERSION.SDK_INT >= 33 && !asked) {
                asked = true
                permission.launch(Manifest.permission.POST_NOTIFICATIONS)
            } else {
                openNotificationSettings(context)
            }
        },
    )
}

@Composable
private fun Divider() = HorizontalDivider(color = Palette.Line)

/** A tappable row that leaves the app (browser, system settings) or opens a dialog. */
@Composable
private fun LinkRow(title: String, description: String, onClick: () -> Unit) {
    Row(
        Modifier
            .fillMaxWidth()
            .heightIn(min = 52.dp)
            .clickable(role = Role.Button, onClick = onClick)
            .padding(vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, color = Palette.Text, fontSize = 14.sp, fontWeight = FontWeight.Medium)
            Text(description, color = Palette.TextDim, fontSize = 11.sp)
        }
        Icon(Icons.AutoMirrored.Filled.KeyboardArrowRight, contentDescription = null, tint = Palette.TextSoft)
    }
}

/**
 * What ships in the APK besides this app's own code. Kept by hand — the dependency list is short (app/build.gradle.kts);
 * add a line here when a library is added there.
 */
private val THIRD_PARTY = listOf(
    "AndroidX (Activity, Core, Lifecycle, Compose, Material 3, Material Icons)" to "Apache License 2.0",
    "Kotlin standard library, kotlinx.coroutines" to "Apache License 2.0",
    "OkHttp, Okio" to "Apache License 2.0",
    "ZXing" to "Apache License 2.0",
    "ZXing Android Embedded" to "Apache License 2.0",
)

@Composable
private fun LicensesDialog(onDismiss: () -> Unit) {
    val context = LocalContext.current
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(R.string.about_licenses)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(stringResource(R.string.about_licenses_intro), fontSize = 13.sp)
                THIRD_PARTY.forEach { (name, license) ->
                    Column {
                        Text(name, fontSize = 13.sp, fontWeight = FontWeight.Medium)
                        Text(license, fontSize = 12.sp)
                    }
                }
                TextButton(onClick = { context.openUrl("https://www.apache.org/licenses/LICENSE-2.0") }) {
                    Text(stringResource(R.string.about_apache_text))
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.update_dialog_close)) } },
    )
}

private fun Context.openUrl(url: String) {
    runCatching { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)) }
}
