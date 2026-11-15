package io.github.jeiel85.claudeusage.ui

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import io.github.jeiel85.claudeusage.R

/**
 * 패키지명을 바꾸기 전(v1.59.2 이하)의 앱이 아직 설치돼 있으면 삭제를 권한다(#254).
 *
 * 두 앱은 Android 에게 서로 다른 앱이라 업데이트로 이어지지 않고 데이터도 옮겨지지 않는다. 둘 다 PC 와 페어링돼 있으면 같은
 * 알림이 두 번 온다. 삭제는 시스템 확인 창(ACTION_DELETE)에 맡긴다. 화면으로 돌아올 때마다 다시 확인하므로 지우면 바로 사라진다.
 * 패키지 가시성(API 30+)은 매니페스트 `<queries><package>` 로 이 패키지 하나만 연다.
 */
@Composable
fun LegacyAppNotice(modifier: Modifier = Modifier) {
    val context = LocalContext.current
    var installed by remember { mutableStateOf(isLegacyInstalled(context)) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { installed = isLegacyInstalled(context) }
    if (!installed) return

    Column(
        modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(12.dp))
            .background(Palette.Accent.copy(alpha = 0.25f))
            .padding(start = 16.dp, end = 8.dp, top = 12.dp, bottom = 4.dp),
    ) {
        Text(stringResource(R.string.legacy_app_title), color = Palette.Text, fontSize = 13.sp, fontWeight = FontWeight.Medium)
        Text(
            stringResource(R.string.legacy_app_body),
            color = Palette.TextSoft,
            fontSize = 12.sp,
            lineHeight = 17.sp,
            modifier = Modifier.padding(top = 4.dp, end = 8.dp),
        )
        TextButton(onClick = { uninstallLegacy(context) }) { Text(stringResource(R.string.legacy_app_remove)) }
    }
}

/** 패키지명을 바꾸기 전 앱의 applicationId. 매니페스트 `<queries>` 와 같아야 한다. */
private const val LEGACY_PACKAGE = "io.github.jeiel85.claudeusage"

private fun isLegacyInstalled(context: Context): Boolean = try {
    context.packageManager.getPackageInfo(LEGACY_PACKAGE, 0)
    true
} catch (_: PackageManager.NameNotFoundException) {
    false
}

private fun uninstallLegacy(context: Context) {
    try {
        context.startActivity(Intent(Intent.ACTION_DELETE, Uri.fromParts("package", LEGACY_PACKAGE, null)))
    } catch (_: ActivityNotFoundException) {
        // 삭제 창을 띄울 수 없는 기기: 앱 정보 화면에서 직접 지우도록 그 화면을 연다.
        context.startActivity(
            Intent(android.provider.Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", LEGACY_PACKAGE, null)),
        )
    }
}
