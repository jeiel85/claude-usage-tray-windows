package io.github.jeiel85.claudeusage.update

import java.io.File
import java.net.HttpURLConnection
import java.net.URL

/**
 * 사용자가 "업데이트" 를 눌렀을 때만 도는 APK 다운로드(Homebase 이식). [UpdateChecker] 와 합치지 않은 이유: 저쪽은 수백 바이트
 * JSON 을 매일 자동으로, 이쪽은 수 MB 실행 파일을 사용자가 눌렀을 때만 받는다 — 실패를 숨기는 배경 작업과 실패를 보여 주고
 * 재시도를 묻는 사용자 작업은 성격이 다르다. 호출부가 IO 디스패처에서 부른다.
 */
internal class UpdateDownloader {

    sealed interface Result {
        data class Success(val file: File) : Result
        data object Failed : Result
    }

    /**
     * Input : URL, 저장할 파일, manifest 의 SHA-256
     * Output: 해시까지 맞는 파일, 또는 실패
     * 핵심 로직: **받았어도 해시가 틀리면 성공이 아니다.** 시도마다 목적지를 먼저 지운다(이전 부분 파일과 섞이지 않게).
     *           일시적인 끊김 한 번으로 실패 화면을 띄우지 않게 3번까지 시도하고, 그래도 안 되면 그때 알린다.
     */
    fun download(url: String, destination: File, expectedSha256Hex: String): Result {
        repeat(MAX_ATTEMPTS) {
            destination.delete()
            if (attempt(url, destination) && Sha256.matches(destination, expectedSha256Hex)) return Result.Success(destination)
        }
        destination.delete()
        return Result.Failed
    }

    /** 응답을 끝까지 받아 파일에 썼는가(해시는 보지 않는다). 모든 실패는 false — 화면은 원인과 상관없이 같다. */
    private fun attempt(url: String, destination: File): Boolean {
        var connection: HttpURLConnection? = null
        return try {
            connection = (URL(url).openConnection() as HttpURLConnection).apply {
                requestMethod = "GET"
                connectTimeout = TIMEOUT_MILLIS
                readTimeout = TIMEOUT_MILLIS // 한 번의 read() 에 대한 것 — 데이터가 들어오는 한 계속 받는다
                useCaches = false
            }
            if (connection.responseCode != HttpURLConnection.HTTP_OK) return false
            destination.parentFile?.mkdirs()
            connection.inputStream.use { input -> destination.outputStream().use { output -> input.copyTo(output) } }
            true
        } catch (_: Exception) {
            false
        } finally {
            connection?.disconnect()
        }
    }

    private companion object {
        const val TIMEOUT_MILLIS = 10_000
        const val MAX_ATTEMPTS = 3
    }
}
