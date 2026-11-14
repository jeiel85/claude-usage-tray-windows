package io.github.jeiel85.claudeusage.data

import android.content.Context
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/**
 * 설정 화면의 "화면" 선택(#230). 컴포즈 상태로 들고 있어 바꾸는 즉시 모든 화면이 다시 그려진다 — 홈 화면이 설정을 따로
 * 다시 읽을 필요가 없다. 값은 SharedPreferences 에 남고, [load] 가 앱 시작 때 한 번 읽는다.
 */
object AppPrefs {
    private const val NAME = "cut_app_prefs"
    private const val KEY_ABSOLUTE_RESET = "absolute_reset_time"
    private const val KEY_KEEP_SCREEN_ON = "keep_screen_on"

    /** 초기화 시각을 "2시간 13분 후" 대신 "오후 3:20" 처럼 시각으로 보인다(PC 앱의 같은 설정과 짝). */
    var absoluteResetTime by mutableStateOf(false)
        private set

    /** 앱이 화면에 있는 동안 화면을 켜 둔다 — 남는 휴대폰을 사용량 화면으로 세워 둘 때. 배터리를 더 쓴다. */
    var keepScreenOn by mutableStateOf(false)
        private set

    private var loaded = false

    /**
     * Input : 컨텍스트
     * Output: 없음
     * 핵심 로직: 프로세스에서 한 번만 읽는다 — 이후 값은 이 객체가 진실이고, 바꿀 때 [setAbsoluteResetTime] 등이 함께 저장한다.
     */
    fun load(context: Context) {
        if (loaded) return
        val prefs = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)
        absoluteResetTime = prefs.getBoolean(KEY_ABSOLUTE_RESET, false)
        keepScreenOn = prefs.getBoolean(KEY_KEEP_SCREEN_ON, false)
        loaded = true
    }

    fun setAbsoluteResetTime(context: Context, value: Boolean) {
        absoluteResetTime = value
        prefs(context).edit().putBoolean(KEY_ABSOLUTE_RESET, value).apply()
    }

    fun setKeepScreenOn(context: Context, value: Boolean) {
        keepScreenOn = value
        prefs(context).edit().putBoolean(KEY_KEEP_SCREEN_ON, value).apply()
    }

    private fun prefs(context: Context) = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)
}
