package io.github.jeiel85.claudeusage.update

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import androidx.core.content.FileProvider
import java.io.File

/**
 * 검증된 APK 를 시스템 설치 앱에 넘긴다(Homebase 이식, Markleaf D079).
 *
 * `PackageInstaller` 세션이 아니라 `ACTION_INSTALL_PACKAGE` 인 이유: 세션으로 자기 자신을 업데이트하면 설치가 끝나는 순간 이 앱
 * 프로세스가 교체되며 조용히 사라진다 — 사용자에게는 "앱이 닫혔다" 로만 보인다. 인텐트로 넘기면 확인·진행·"완료/열기" 화면을
 * 시스템 설치 앱이 자기 프로세스에서 그려 "열기" 한 번으로 새 버전이 뜬다. `ACTION_VIEW` 가 아닌 이유: APK MIME 을 여는 파일
 * 관리자·서드파티 설치 앱이 끼어들 수 있다. 이 액션은 API 29 에서 deprecated 지만 시스템 설치 앱이 그대로 처리한다.
 *
 * 다른 키로 서명된 APK 를 미리 거르지는 않는다 — 잘못 만든 사전 검증이 정상 업데이트를 막는 쪽이 더 나쁘고, 결국 OS 가
 * `INSTALL_FAILED_UPDATE_INCOMPATIBLE` 로 거부한다.
 */
internal object UpdateInstaller {

    /**
     * Input : Activity 컨텍스트, 검증된 APK
     * Output: 시스템 설치 앱을 띄웠는가(설치 자체의 성공 여부가 아니다)
     * 핵심 로직: FileProvider URI 하나에만 읽기 권한을 붙여 넘긴다. 파일은 여기서 지우지 **않는다** — 설치 앱은 이 호출이 돌아온
     *           뒤에 읽는다. 남은 파일은 다음에 [deleteLeftovers] 가 치운다. 띄우지 못했으면 읽을 쪽이 없으니 바로 지운다.
     */
    fun install(context: Context, apkFile: File): Boolean {
        val uri = try {
            FileProvider.getUriForFile(context, "${context.packageName}.fileprovider", apkFile)
        } catch (_: IllegalArgumentException) {
            apkFile.delete() // file_paths.xml 과 코드가 어긋났다 — 넘길 방법이 없다.
            return false
        }
        @Suppress("DEPRECATION")
        val intent = Intent(Intent.ACTION_INSTALL_PACKAGE)
            .setDataAndType(uri, APK_MIME_TYPE)
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
        return try {
            context.startActivity(intent)
            true
        } catch (_: ActivityNotFoundException) {
            apkFile.delete() // 시스템 설치 앱이 없거나 꺼진 기기 — 호출부가 "브라우저에서 열기" 를 제시한다.
            false
        }
    }

    /** 지난번 설치 앱에 넘기고 남긴 APK 를 지운다(설치를 마쳤든 취소했든 더 쓸 데가 없다). IO 스레드에서 부른다. */
    fun deleteLeftovers(downloadDir: File) {
        downloadDir.listFiles()?.forEach { it.delete() }
    }

    private const val APK_MIME_TYPE = "application/vnd.android.package-archive"
}
