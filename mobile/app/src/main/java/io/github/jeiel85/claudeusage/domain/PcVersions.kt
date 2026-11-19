package io.github.jeiel85.claudeusage.domain

/**
 * PC 앱 버전 안내(#299). PC 탭은 각 PC 가 실시간 스냅샷에 실어 보내는 `appVersion` 으로 오래된 PC 를 알린다 —
 * 휴대폰 앱만 업데이트해서는 바뀌지 않는 동작이 있어서다.
 */
object PcVersions {
    /**
     * v1.68.0(#297)부터 PC 가 알림마다 "울릴지" 를 우선순위로 정한다. 그보다 오래된 PC 는 날씨 예보 같은 일상 알림도
     * `warning` 으로 보내, 이 앱의 조용한 채널을 거치지 않고 소리·진동을 낸다. PC 쪽 동작이 휴대폰에 영향을 주는
     * 변경이 또 생기면 이 값을 올린다.
     */
    const val RECOMMENDED_MIN = "1.68.0"

    /**
     * Input : PC 가 보낸 버전(`1.67.3`, 접두사 `v`·꼬리 `-beta`·`+build` 허용)
     * Output: [min] 보다 낮으면 true. 비었거나 읽을 수 없으면 false — 모르는 버전으로 사용자를 재촉하지 않는다.
     */
    fun isOutdated(appVersion: String?, min: String = RECOMMENDED_MIN): Boolean {
        val version = parse(appVersion) ?: return false
        val minimum = parse(min) ?: return false
        for (i in 0 until maxOf(version.size, minimum.size)) {
            val a = version.getOrElse(i) { 0 }
            val b = minimum.getOrElse(i) { 0 }
            if (a != b) return a < b
        }
        return false
    }

    private fun parse(value: String?): List<Int>? {
        val core = value?.trim()?.removePrefix("v")?.substringBefore('+')?.substringBefore('-')
        if (core.isNullOrEmpty()) return null
        val parts = core.split('.')
        if (parts.size > 4) return null
        return parts.map { it.toIntOrNull()?.takeIf { n -> n >= 0 } ?: return null }
    }
}
