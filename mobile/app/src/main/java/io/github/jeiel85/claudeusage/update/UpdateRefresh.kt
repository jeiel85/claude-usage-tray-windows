package io.github.jeiel85.claudeusage.update

import android.os.Build
import io.github.jeiel85.claudeusage.BuildConfig

/**
 * 홈 배너와 배경 확인([UpdateCheckJobService])이 함께 쓰는 판단 한 벌(Homebase 이식). 같은 캐시·같은 하루 1회 규칙이라,
 * 화면을 열었든 배경에서 돌았든 네트워크 확인은 하루에 한 번이다.
 */
internal object UpdateRefresh {

    /**
     * Input : [UpdatePreferences]
     * Output: 지금 알릴 [UpdateManifest], 없으면 null
     * 핵심 로직: 네트워크는 하루 1회만, 판단은 캐시된 원문으로 매번 다시 한다. **온전히 파싱되는 응답만 저장하고 그때만
     *           확인 시각을 찍는다.** 블로킹 네트워크 호출이 있으므로 메인 스레드 밖에서 부른다.
     */
    fun run(preferences: UpdatePreferences): UpdateManifest? {
        if (!preferences.isEnabled()) return null
        val now = System.currentTimeMillis()
        if (UpdateDecision.shouldCheckNow(preferences.lastCheckedAt(), now)) {
            UpdateChecker().fetchRaw()?.let { raw ->
                if (parse(raw) != null) {
                    preferences.setCachedManifest(raw)
                    preferences.setLastCheckedAt(now)
                }
            }
        }
        val manifest = preferences.cachedManifest()?.let(::parse) ?: return null
        return manifest.takeIf { shouldOffer(it, preferences) }
    }

    /** 사용자가 직접 요청한 확인([runNow])의 결과 — 화면이 무엇을 말할지 고르는 데 쓴다. */
    sealed interface ManualResult {
        data class Offer(val manifest: UpdateManifest) : ManualResult
        data object UpToDate : ManualResult
        data object Failed : ManualResult
        data object Disabled : ManualResult
    }

    /**
     * Input : [UpdatePreferences]
     * Output: [ManualResult]
     * 핵심 로직: "지금 확인" 을 눌렀을 때. 사용자가 **지금** 원한 것이라 하루 1회 규칙을 건너뛴다. 저장 규칙은 [run] 과 같다.
     *           건너뛴 버전은 여기서 풀어 준다 — 되돌릴 다른 길이 없고, 직접 확인을 요청한 사람에게 "최신" 이라 답하면서
     *           새 버전을 숨기는 것은 틀린 답이다. 확인이 꺼져 있으면 네트워크를 타지 않는다.
     */
    fun runNow(preferences: UpdatePreferences): ManualResult {
        if (!preferences.isEnabled()) return ManualResult.Disabled
        val raw = UpdateChecker().fetchRaw() ?: return ManualResult.Failed
        val manifest = parse(raw) ?: return ManualResult.Failed
        preferences.setCachedManifest(raw)
        preferences.setLastCheckedAt(System.currentTimeMillis())
        if (preferences.skippedVersionCode() != 0) preferences.setSkippedVersionCode(0)
        return if (shouldOffer(manifest, preferences)) ManualResult.Offer(manifest) else ManualResult.UpToDate
    }

    private fun parse(raw: String): UpdateManifest? = UpdateManifestParser.parse(raw, BuildConfig.UPDATE_ALLOW_HTTP)

    private fun shouldOffer(manifest: UpdateManifest, preferences: UpdatePreferences): Boolean =
        UpdateDecision.shouldOffer(
            manifest = manifest,
            installedVersionCode = BuildConfig.VERSION_CODE,
            skippedVersionCode = preferences.skippedVersionCode(),
            deviceSdk = Build.VERSION.SDK_INT,
        )
}
