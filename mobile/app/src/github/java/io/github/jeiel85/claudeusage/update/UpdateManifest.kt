package io.github.jeiel85.claudeusage.update

import org.json.JSONObject

/**
 * GitHub Release 의 `update.json` 자산(#228). 릴리스 워크플로가 방금 만든 APK 에서 직접 뽑아 넣는다 —
 * 버전·minSdk 는 APK 의 badging 에서, 크기와 해시는 붙일 파일 자체에서. Homebase `UpdateManifest`
 * (Markleaf D075 에서 온 구조) 이식.
 */
internal data class UpdateManifest(
    val versionCode: Int,
    val versionName: String,
    val apkUrl: String,
    val apkSizeBytes: Long,
    val sha256: String,
    val minSdk: Int,
    val releaseNotesUrl: String,
)

internal object UpdateManifestParser {

    /**
     * Input : `update.json` 본문, http 를 허용할지(디버그 검증 빌드만 true)
     * Output: 온전한 [UpdateManifest], 조금이라도 이상하면 null
     * 핵심 로직: **의심스러우면 null.** 업데이트 확인은 사용자가 요청하지 않은 배경 작업이라 실패를 보여 줄 곳이 없다 —
     *           빠진 필드·타입 불일치·음수·깨진 JSON 이 모두 "이번엔 조용히 넘어간다" 로 모인다.
     *           https 를 강제하는 이유: 이 URL 로 앱이 직접 APK 를 받으므로, 평문을 받아들이면 중간자가 주소를 바꿀 수 있다.
     *           호스트를 고정하지 않는 것은 릴리스 자산이 GitHub 리다이렉트를 타기 때문 — 그쪽 방어는 SHA-256 대조와
     *           OS 의 서명 검증이 맡는다. allowHttp 를 인자로 받는 것은 BuildConfig 없이 JVM 테스트로 이 규칙을 고정하려고.
     */
    fun parse(raw: String, allowHttp: Boolean): UpdateManifest? = try {
        val json = JSONObject(raw)
        UpdateManifest(
            versionCode = json.getInt(KEY_VERSION_CODE),
            versionName = json.getString(KEY_VERSION_NAME),
            apkUrl = json.getString(KEY_APK_URL),
            apkSizeBytes = json.getLong(KEY_APK_SIZE),
            sha256 = json.getString(KEY_SHA256),
            minSdk = json.getInt(KEY_MIN_SDK),
            releaseNotesUrl = json.getString(KEY_RELEASE_NOTES_URL),
        ).takeIf { it.isUsable(allowHttp) }
    } catch (_: Exception) {
        null
    }

    private fun UpdateManifest.isUsable(allowHttp: Boolean): Boolean =
        versionCode > 0 &&
            versionName.isNotBlank() &&
            isAllowedUrl(apkUrl, allowHttp) &&
            apkSizeBytes > 0 &&
            SHA256_HEX.matches(sha256) &&
            minSdk > 0 &&
            isAllowedUrl(releaseNotesUrl, allowHttp)

    /** https 만. 디버그 검증 빌드(`-Pcut.updateAllowHttp=true`)만 로컬 http 서버를 허용한다. */
    fun isAllowedUrl(url: String, allowHttp: Boolean): Boolean =
        url.startsWith(HTTPS) || (allowHttp && url.startsWith(HTTP))

    private val SHA256_HEX = Regex("^[0-9a-fA-F]{64}$")
    private const val HTTPS = "https://"
    private const val HTTP = "http://"
    private const val KEY_VERSION_CODE = "versionCode"
    private const val KEY_VERSION_NAME = "versionName"
    private const val KEY_APK_URL = "apkUrl"
    private const val KEY_APK_SIZE = "apkSizeBytes"
    private const val KEY_SHA256 = "sha256"
    private const val KEY_MIN_SDK = "minSdk"
    private const val KEY_RELEASE_NOTES_URL = "releaseNotesUrl"
}
