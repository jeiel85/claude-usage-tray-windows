package io.github.jeiel85.claudeusage.update

import android.content.Context

/**
 * 업데이트 확인이 기억하는 값들(Homebase 이식). 읽는 쪽이 코루틴 밖(컴포지션·잡 시작)이라 SharedPreferences.
 */
internal class UpdatePreferences(context: Context) {

    private val prefs = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** 사이드로드 전용 앱이라 기본 켜짐(Homebase M-4). 알림 탭에서 끌 수 있다 — GitHub 로 나가는 요청 자체에 대한 동의다. */
    fun isEnabled(): Boolean = prefs.getBoolean(KEY_ENABLED, true)

    fun setEnabled(enabled: Boolean) = prefs.edit().putBoolean(KEY_ENABLED, enabled).apply()

    /** 마지막으로 확인에 **성공한** 시각. 실패는 기록하지 않는다 — 실패로 하루를 쓰면 잠깐 오프라인이던 사용자가 다음 날까지 못 본다. */
    fun lastCheckedAt(): Long = prefs.getLong(KEY_LAST_CHECKED_AT, 0L)

    fun setLastCheckedAt(at: Long) = prefs.edit().putLong(KEY_LAST_CHECKED_AT, at).apply()

    /** 마지막으로 받은 온전한 `update.json` 원문. 확인은 하루 1회지만 배너는 그 사이에도 떠 있어야 한다. */
    fun cachedManifest(): String? = prefs.getString(KEY_CACHED_MANIFEST, null)

    fun setCachedManifest(raw: String) = prefs.edit().putString(KEY_CACHED_MANIFEST, raw).apply()

    /** "이 버전 건너뛰기" 를 누른 versionCode, 없으면 0. */
    fun skippedVersionCode(): Int = prefs.getInt(KEY_SKIPPED_VERSION_CODE, 0)

    fun setSkippedVersionCode(versionCode: Int) = prefs.edit().putInt(KEY_SKIPPED_VERSION_CODE, versionCode).apply()

    /** 배경 확인이 Android 알림을 띄운 versionCode, 없으면 0([UpdateDecision.shouldNotify]). */
    fun notifiedVersionCode(): Int = prefs.getInt(KEY_NOTIFIED_VERSION_CODE, 0)

    fun setNotifiedVersionCode(versionCode: Int) = prefs.edit().putInt(KEY_NOTIFIED_VERSION_CODE, versionCode).apply()

    private companion object {
        const val NAME = "cut_update"
        const val KEY_ENABLED = "enabled"
        const val KEY_LAST_CHECKED_AT = "last_checked_at"
        const val KEY_CACHED_MANIFEST = "cached_manifest"
        const val KEY_SKIPPED_VERSION_CODE = "skipped_version_code"
        const val KEY_NOTIFIED_VERSION_CODE = "notified_version_code"
    }
}
