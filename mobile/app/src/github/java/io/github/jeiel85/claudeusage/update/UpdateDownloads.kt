package io.github.jeiel85.claudeusage.update

import android.content.Context
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.io.File

/**
 * 진행 중인 업데이트 다운로드 하나를 프로세스 전체에서 들고 있는다(#228, Codex 리뷰).
 *
 * 왜 화면이 아니라 여기인가: 다운로드를 업데이트 창의 LaunchedEffect 에서 돌리면, 화면을 돌리는 순간 액티비티가 다시 만들어지며
 * 그 코루틴이 취소된다. 블로킹 다운로드는 끝까지 돌지만 결과가 버려져 설치 화면이 뜨지 않고, 새 창은 진행 상태를 잃는다.
 * 여기서 돌리면 다운로드는 화면과 무관하게 끝나고, 다시 만들어진 창은 [state] 를 보고 그대로 이어 간다(내려받는 중이면 진행 표시,
 * 끝났으면 바로 설치 화면).
 *
 * 설치 화면을 여는 일(액티비티 컨텍스트가 필요한 일)은 여기서 하지 않는다 — 그건 창이 화면에 보일 때 한다.
 */
internal object UpdateDownloads {

    sealed interface State {
        data object Idle : State
        data class Running(val versionCode: Int) : State
        data class Ready(val versionCode: Int, val file: File) : State
        data class Failed(val versionCode: Int) : State
    }

    private const val DOWNLOAD_DIR = "updates"
    private const val DOWNLOAD_FILE_NAME = "update.apk"

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private val _state = MutableStateFlow<State>(State.Idle)
    val state: StateFlow<State> = _state.asStateFlow()

    /** 받은 APK 를 두는 앱 캐시 폴더(FileProvider `file_paths.xml` 의 `updates/`). */
    fun directory(context: Context): File = File(context.cacheDir, DOWNLOAD_DIR)

    /**
     * Input : 컨텍스트(애플리케이션 컨텍스트만 쓴다), 받을 버전
     * Output: 새로 시작했는가 — 이미 받는 중이면 false(같은 캐시 파일을 두 흐름이 쓰지 않게)
     * 핵심 로직: 상태를 Running 으로 바꾼 뒤 IO 에서 받고, SHA-256 까지 맞으면 Ready, 아니면 Failed.
     */
    fun start(context: Context, manifest: UpdateManifest): Boolean {
        synchronized(this) {
            if (_state.value is State.Running) return false
            _state.value = State.Running(manifest.versionCode)
        }
        val dir = directory(context.applicationContext)
        scope.launch {
            val destination = File(dir.apply { mkdirs() }, DOWNLOAD_FILE_NAME)
            _state.value = when (val result = UpdateDownloader().download(manifest.apkUrl, destination, manifest.sha256)) {
                is UpdateDownloader.Result.Success -> State.Ready(manifest.versionCode, result.file)
                UpdateDownloader.Result.Failed -> State.Failed(manifest.versionCode)
            }
        }
        return true
    }

    /** 설치 화면에 넘겼거나(성공·실패 모두) 사용자가 실패를 닫았을 때 처음 상태로 돌린다. 받는 중에는 아무것도 하지 않는다. */
    fun reset() {
        synchronized(this) {
            if (_state.value !is State.Running) _state.value = State.Idle
        }
    }

    /** 받은 파일을 설치 앱에 넘기지 못했을 때 — 창이 "다시 시도·브라우저" 를 보이게 한다. */
    fun fail(versionCode: Int) {
        synchronized(this) {
            if (_state.value !is State.Running) _state.value = State.Failed(versionCode)
        }
    }

    /** 받는 중이거나 아직 설치 화면에 넘기지 않은 파일이 있는가 — 그때는 남은 파일 정리를 건너뛴다. */
    val holdsFile: Boolean get() = _state.value.let { it is State.Running || it is State.Ready }
}
