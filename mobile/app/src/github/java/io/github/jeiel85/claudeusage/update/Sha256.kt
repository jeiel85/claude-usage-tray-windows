package io.github.jeiel85.claudeusage.update

import java.io.File
import java.security.MessageDigest

/**
 * 받은 APK 가 manifest 의 `sha256` 과 같은 파일인지 대조한다(Homebase 이식).
 * 서명은 결국 OS 가 확인하지만 그 실패(`INSTALL_FAILED_UPDATE_INCOMPATIBLE`)는 설치 확인 팝업까지 다 띄운 **뒤에** 나서
 * "받았는데 이유 없이 거부" 로 보인다. 여기서 먼저 걸러 "다운로드가 손상됐다" 는 분명한 이유로 멈춘다.
 */
internal object Sha256 {

    /** 파일 전체를 메모리에 올리지 않고 스트리밍으로 해시한다. 소문자 hex 64자. */
    fun of(file: File): String {
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(64 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read <= 0) break
                digest.update(buffer, 0, read)
            }
        }
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    /** hex 대소문자는 관례일 뿐 표준이 아니라 무시한다. */
    fun matches(file: File, expectedHex: String): Boolean = of(file).equals(expectedHex, ignoreCase = true)
}
