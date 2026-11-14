package io.github.jeiel85.claudeusage.update

import java.util.concurrent.TimeUnit

/**
 * "확인할 때인가" 와 "알릴 만한가" 판단(Homebase 이식). 순수 함수만 두는 이유: 이 판단이 틀리면 가장 거슬린다 —
 * 너무 자주 확인하면 배경 트래픽이 되고, 느슨하면 이미 최신인 사용자에게 배너를 띄운다. 네트워크·저장소를 섞지 않아야
 * 그 규칙을 테스트로 고정할 수 있다. 상태(마지막 확인 시각, 건너뛴 버전)는 인자로 받고 저장은 호출부의 몫이다.
 */
internal object UpdateDecision {

    val CHECK_INTERVAL_MILLIS: Long = TimeUnit.DAYS.toMillis(1)

    /**
     * Input : 마지막으로 확인에 성공한 시각, 지금(epoch ms), 간격
     * Output: 지금 네트워크 확인을 해도 되는가
     * 핵심 로직: 확인한 적이 없으면(0) 한다. 미래 시각이면 한다 — 기기 시계가 앞당겨졌다 돌아오면 `now - last` 가
     *           음수가 되어 영원히 확인하지 않는 상태에 갇힐 수 있다. 조용히 죽어 있는 것보다 한 번 더 확인하는 편이 낫다.
     */
    fun shouldCheckNow(lastCheckedAt: Long, now: Long, interval: Long = CHECK_INTERVAL_MILLIS): Boolean {
        if (lastCheckedAt <= 0L) return true
        if (lastCheckedAt > now) return true
        return now - lastCheckedAt >= interval
    }

    /**
     * Input : 받은 manifest, 설치된 versionCode, 건너뛴 versionCode(없으면 0), 기기 SDK
     * Output: 배너를 띄울 새 버전인가
     * 핵심 로직: versionCode **정수 비교**만 쓴다(문자열 비교는 1.9.0 을 1.10.0 보다 크게 만든다). 같거나 낮으면 멈춘다 —
     *           로컬에서 올린 개발 빌드에 "구버전으로 업데이트" 를 띄우지 않는다. minSdk 가 기기보다 높으면 설치가 실패할 것을
     *           미리 아는 경우라 거른다.
     */
    fun shouldOffer(manifest: UpdateManifest, installedVersionCode: Int, skippedVersionCode: Int, deviceSdk: Int): Boolean {
        if (manifest.versionCode <= installedVersionCode) return false
        if (manifest.versionCode == skippedVersionCode) return false
        if (deviceSdk < manifest.minSdk) return false
        return true
    }

    /**
     * Input : 알릴 만한 versionCode, 이미 알림을 띄운 versionCode(없으면 0)
     * Output: 지금 Android 알림을 띄울까
     * 핵심 로직: **한 버전에 알림은 한 번.** 배경 확인은 하루에도 여러 번 돌고 캐시로 같은 판정을 반복하므로, 이 규칙이 없으면
     *           같은 새 버전을 매번 다시 알린다. 사용자가 알림을 지웠다면 그것도 답이다 — 앱 안 배너는 남는다.
     */
    fun shouldNotify(offeredVersionCode: Int, notifiedVersionCode: Int): Boolean = offeredVersionCode != notifiedVersionCode
}
