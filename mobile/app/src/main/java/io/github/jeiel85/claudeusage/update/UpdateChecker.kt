package io.github.jeiel85.claudeusage.update

import io.github.jeiel85.claudeusage.BuildConfig
import java.net.HttpURLConnection
import java.net.URL

/**
 * 자동으로 도는 유일한 업데이트 네트워크 동작: 정적 JSON 하나를 GET 한다(Homebase 이식, #228).
 * 사용자가 설치를 눌렀을 때만 도는 APK 다운로드는 [UpdateDownloader] 다.
 *
 * 요청에는 URL 과 표준 헤더뿐 — 쿼리스트링·쿠키·기기 식별자를 싣지 않는다.
 *
 * `api.github.com` 을 부르지 않는 이유: 비인증 REST 한도(시간당 60건)가 앱이 아니라 **IP** 에 붙어, 통신사 NAT 뒤에서는
 * 남이 쓴 한도에 막힌다. 읽는 것은 REST API 가 아니라 릴리스 자산 하나이고 스키마도 우리가 정한다.
 */
internal class UpdateChecker(private val manifestUrl: String = BuildConfig.UPDATE_MANIFEST_URL) {

    /**
     * Input : 없음
     * Output: 응답 본문, 무엇이 잘못되든 null
     * 핵심 로직: **모든 실패는 null.** 오프라인·DNS·타임아웃·404(정식 서명 전 릴리스에는 update.json 이 없다)·5xx 가 전부
     *           "조용히 넘어간다" 로 모인다. 본문을 64KB 로 자르는 이유: 이 JSON 은 수백 바이트다 — 그보다 크면 우리가 올린
     *           파일이 아니고, 끝나지 않는 응답에 메모리를 내주지 않는다. 파싱하지 않고 원문을 돌려주는 것은 캐시해 두고
     *           다음 실행에서 네트워크 없이 같은 판단을 하려고.
     */
    fun fetchRaw(): String? {
        if (!UpdateManifestParser.isAllowedUrl(manifestUrl, BuildConfig.UPDATE_ALLOW_HTTP)) return null
        var connection: HttpURLConnection? = null
        return try {
            connection = (URL(manifestUrl).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = TIMEOUT_MILLIS
                readTimeout = TIMEOUT_MILLIS
                useCaches = false
            }
            if (connection.responseCode != HttpURLConnection.HTTP_OK) null
            else connection.inputStream.bufferedReader().use { reader ->
                val buffer = CharArray(MAX_BODY_CHARS)
                var total = 0
                while (total < buffer.size) {
                    val read = reader.read(buffer, total, buffer.size - total)
                    if (read <= 0) break
                    total += read
                }
                String(buffer, 0, total)
            }
        } catch (_: Exception) {
            null
        } finally {
            connection?.disconnect()
        }
    }

    private companion object {
        const val TIMEOUT_MILLIS = 10_000
        const val MAX_BODY_CHARS = 64 * 1024
    }
}
