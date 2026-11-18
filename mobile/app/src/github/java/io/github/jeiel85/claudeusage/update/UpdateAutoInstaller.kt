package io.github.jeiel85.claudeusage.update

import android.app.ActivityManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageInstaller
import android.content.pm.PackageManager
import android.content.pm.Signature
import android.os.Build
import android.util.Log
import androidx.core.content.IntentCompat
import io.github.jeiel85.claudeusage.BuildConfig
import java.io.File

/**
 * 배경 확인이 찾은 새 버전을 사용자 손을 거치지 않고 설치한다(#281, 포너디 D-7 방식).
 *
 * 수동 설치([UpdateInstaller])와 다른 길을 쓰는 이유: 사용자가 누른 설치는 시스템 설치 앱이 "열기" 까지 보여 주는 편이 낫지만,
 * 아무도 보고 있지 않을 때는 그 화면을 띄울 수 없다. `PackageInstaller` 세션은 Android 12+ 에서 `setRequireUserAction(
 * USER_ACTION_NOT_REQUIRED)` 와 `UPDATE_PACKAGES_WITHOUT_USER_ACTION` 권한으로 **자기 자신의 업데이트**를 확인 없이 설치한다.
 * 시스템이 그래도 확인을 원하면(Android 11 이하, '알 수 없는 앱 설치' 미허용 등) 그 확인 화면으로 가는 알림 한 건으로 넘긴다.
 *
 * 설치가 끝나는 순간 이 프로세스가 새 버전으로 교체되므로 앱이 화면에 떠 있으면 하지 않는다 — 쓰던 화면이 말없이 닫힌다.
 * 다운로드는 SHA-256 에 더해 같은 패키지·더 높은 versionCode·**같은 서명 키**까지 본다. OS 도 서명이 다른 업데이트는 거부하지만,
 * 미리 거르면 실패할 설치로 사용자에게 확인을 묻는 일이 없다. 여기서 틀려도 결과는 "평소처럼 알림" 이라 수동 설치 길은 남는다.
 */
internal object UpdateAutoInstaller {
    private const val TAG = "CutAutoUpdate"
    private const val DIR = "auto-update" // 수동 흐름의 cache/updates 와 따로 — 화면이 남은 파일을 지울 때 섞이지 않게
    private const val ACTION_STATUS = "io.github.jeiel85.claudeusage.update.AUTO_INSTALL_STATUS"
    private const val EXTRA_VERSION_CODE = "versionCode"
    private const val EXTRA_VERSION_NAME = "versionName"

    enum class Outcome {
        /** 앱이 화면에 있거나 그사이 설정을 껐다 — 다음 주기에 다시 본다. 알리지 않는다. */
        Deferred,

        /** 설치 세션을 넘겼다. 결과는 [UpdateInstallReceiver] 로 온다. */
        Committed,

        /** 받거나 확인하거나 넘기지 못했다 — 평소처럼 알림으로 알린다. 이 버전은 다시 자동으로 시도하지 않는다. */
        Failed,
    }

    /**
     * Input : 컨텍스트, 설치할 버전, [UpdatePreferences]
     * Output: [Outcome]
     * 핵심 로직: 화면 확인 → 받기(SHA-256) → APK 확인 → 화면·설정 재확인 → 세션 커밋. 시도한 버전은 받기·확인에 실패해도 기억한다 —
     *           깨진 릴리스를 12시간마다 다시 받지 않게. 미루는 경우(화면·설정)만 기억하지 않는다. 블로킹 네트워크라 배경 스레드에서 부른다.
     */
    fun run(context: Context, manifest: UpdateManifest, preferences: UpdatePreferences): Outcome {
        if (isAppVisible()) return Outcome.Deferred
        val dir = File(context.cacheDir, DIR).apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() }
        val apk = File(dir, "update.apk")
        try {
            val downloaded = UpdateDownloader().download(manifest.apkUrl, apk, manifest.sha256)
            if (downloaded !is UpdateDownloader.Result.Success) {
                preferences.setAutoInstallAttemptedVersionCode(manifest.versionCode)
                return Outcome.Failed
            }
            if (!isSameAppNewerAndSameSigner(context, apk)) {
                Log.w(TAG, "downloaded APK is not a same-signer update of this app; leaving it to the manual flow")
                preferences.setAutoInstallAttemptedVersionCode(manifest.versionCode)
                return Outcome.Failed
            }
            // 받는 동안 앱을 열었거나 설정을 껐을 수 있다.
            if (isAppVisible() || !preferences.isEnabled() || !preferences.isAutoInstallEnabled()) return Outcome.Deferred
            preferences.setAutoInstallAttemptedVersionCode(manifest.versionCode)
            preferences.setInstallingVersionCode(manifest.versionCode)
            commit(context, apk, manifest)
            return Outcome.Committed
        } catch (e: Exception) {
            Log.w(TAG, "auto install failed", e)
            preferences.setAutoInstallAttemptedVersionCode(manifest.versionCode)
            preferences.setInstallingVersionCode(0)
            return Outcome.Failed
        } finally {
            apk.delete() // 세션은 커밋 전에 바이트를 다 복사해 갔다.
        }
    }

    /** 이 앱의 액티비티가 보이는가. 포그라운드 서비스(UnifiedPush 의 잠깐 올리기)나 잡 실행만으로는 보이는 것이 아니다. */
    private fun isAppVisible(): Boolean {
        val info = ActivityManager.RunningAppProcessInfo()
        ActivityManager.getMyMemoryState(info)
        return info.importance == ActivityManager.RunningAppProcessInfo.IMPORTANCE_FOREGROUND ||
            info.importance == ActivityManager.RunningAppProcessInfo.IMPORTANCE_VISIBLE
    }

    /** 같은 패키지, 더 높은 versionCode, 설치된 앱과 같은 서명 키 집합. */
    private fun isSameAppNewerAndSameSigner(context: Context, apk: File): Boolean {
        val pm = context.packageManager
        @Suppress("DEPRECATION")
        val flags = if (Build.VERSION.SDK_INT >= 28) PackageManager.GET_SIGNING_CERTIFICATES else PackageManager.GET_SIGNATURES
        val archive = pm.getPackageArchiveInfo(apk.path, flags) ?: return false
        val installed = pm.getPackageInfo(context.packageName, flags)
        if (archive.packageName != context.packageName) return false
        if (versionCode(archive) <= versionCode(installed)) return false
        val theirs = signers(archive)
        return theirs.isNotEmpty() && theirs == signers(installed)
    }

    @Suppress("DEPRECATION")
    private fun versionCode(info: PackageInfo): Long =
        if (Build.VERSION.SDK_INT >= 28) info.longVersionCode else info.versionCode.toLong()

    @Suppress("DEPRECATION")
    private fun signers(info: PackageInfo): Set<String> {
        val signatures: Array<Signature>? =
            if (Build.VERSION.SDK_INT >= 28) info.signingInfo?.apkContentsSigners else info.signatures
        return signatures.orEmpty().map { it.toCharsString() }.toSet()
    }

    private fun commit(context: Context, apk: File, manifest: UpdateManifest) {
        val installer = context.packageManager.packageInstaller
        // 지난번에 확인을 기다리다 남은 세션 — 새 버전으로 대신한다.
        installer.mySessions.forEach { runCatching { installer.abandonSession(it.sessionId) } }
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL).apply {
            setAppPackageName(context.packageName)
            setSize(apk.length())
            if (Build.VERSION.SDK_INT >= 31) setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED)
        }
        val id = installer.createSession(params)
        try {
            installer.openSession(id).use { session ->
                session.openWrite("base.apk", 0, apk.length()).use { out ->
                    apk.inputStream().use { it.copyTo(out) }
                    session.fsync(out)
                }
                val status = Intent(context, UpdateInstallReceiver::class.java)
                    .setAction(ACTION_STATUS)
                    .putExtra(EXTRA_VERSION_CODE, manifest.versionCode)
                    .putExtra(EXTRA_VERSION_NAME, manifest.versionName)
                // 시스템이 결과(EXTRA_STATUS·확인 인텐트)를 채워 넣으므로 mutable 이어야 한다. 명시적 인텐트라 안전하다.
                val mutable = if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0
                val pending = PendingIntent.getBroadcast(context, id, status, PendingIntent.FLAG_UPDATE_CURRENT or mutable)
                session.commit(pending.intentSender)
            }
        } catch (e: Exception) {
            runCatching { installer.abandonSession(id) }
            throw e
        }
    }

    // ---- 결과 ([UpdateInstallReceiver]) ---------------------------------------------------------------------------

    internal fun onStatus(context: Context, intent: Intent) {
        if (intent.action != ACTION_STATUS) return
        val preferences = UpdatePreferences(context)
        val versionCode = intent.getIntExtra(EXTRA_VERSION_CODE, 0)
        val versionName = intent.getStringExtra(EXTRA_VERSION_NAME).orEmpty()
        when (val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)) {
            PackageInstaller.STATUS_SUCCESS -> Unit // 곧 새 프로세스에서 MY_PACKAGE_REPLACED 가 온다.
            // 우리가 새 세션을 만들며 버린 옛 세션이거나, 사용자가 확인 화면에서 취소했다. 앞의 경우 지금 진행 중인 설치의 기록을
            // 지우면 안 되고(에뮬레이터에서 실제로 "업데이트했습니다" 알림이 사라졌다), 뒤의 경우 사용자의 답이라 다시 알리지 않는다.
            PackageInstaller.STATUS_FAILURE_ABORTED -> Unit
            PackageInstaller.STATUS_PENDING_USER_ACTION -> {
                // 사용자가 직접 마치는 설치다 — "자동으로 설치했다" 고 알리지 않는다.
                preferences.setInstallingVersionCode(0)
                val confirm = IntentCompat.getParcelableExtra(intent, Intent.EXTRA_INTENT, Intent::class.java)
                if (confirm != null && UpdateNotifier.showConfirm(context, versionName, confirm)) {
                    preferences.setNotifiedVersionCode(versionCode)
                } else {
                    UpdateCheckJobService.notifyOnce(context, preferences)
                }
            }
            else -> {
                Log.w(TAG, "auto install failed: $status ${intent.getStringExtra(PackageInstaller.EXTRA_STATUS_MESSAGE)}")
                preferences.setInstallingVersionCode(0)
                UpdateCheckJobService.notifyOnce(context, preferences)
            }
        }
    }

    /** 패키지가 교체된 뒤 새 프로세스에서. 자동 설치로 이 버전이 됐으면 한 번 알린다. */
    internal fun onReplaced(context: Context) {
        val preferences = UpdatePreferences(context)
        val installing = preferences.installingVersionCode()
        if (installing == 0) return
        preferences.setInstallingVersionCode(0)
        File(context.cacheDir, DIR).listFiles()?.forEach { it.delete() }
        if (installing == BuildConfig.VERSION_CODE) UpdateNotifier.showUpdated(context, BuildConfig.VERSION_NAME)
    }
}

/** 자동 설치 세션의 결과와 "방금 새 버전으로 바뀌었다" 를 받는다(github 빌드 매니페스트에만 등록). */
class UpdateInstallReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action == Intent.ACTION_MY_PACKAGE_REPLACED) UpdateAutoInstaller.onReplaced(context)
        else UpdateAutoInstaller.onStatus(context, intent)
    }
}
