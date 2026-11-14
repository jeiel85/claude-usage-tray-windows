package io.github.jeiel85.claudeusage.update

import android.app.job.JobInfo
import android.app.job.JobParameters
import android.app.job.JobScheduler
import android.app.job.JobService
import android.content.ComponentName
import android.content.Context
import android.util.Log
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicBoolean

/**
 * 앱을 열지 않아도 새 버전을 알게 하는 배경 확인(Homebase M-22 이식, #228).
 * 플랫폼 JobScheduler 를 쓴다 — 이 일 하나에 WorkManager 의존성을 들일 이유가 없다. 네트워크가 있을 때 12시간마다 깨어나되
 * 실제 GET 은 [UpdateRefresh] 가 하루 1회로 거른다. 주기를 하루보다 짧게 잡는 이유: 마지막 확인 23시간 뒤에 깨면 이번엔
 * 건너뛰므로 주기가 하루면 최악 48시간, 12시간이면 최악 36시간이다.
 */
class UpdateCheckJobService : JobService() {

    // onStopJob(설정을 꺼서 취소된 경우 포함)이 켠다. 블로킹 GET 은 중간에 못 끊으므로 알림 직전에 이 값을 본다.
    private val stopped = AtomicBoolean(false)

    override fun onStartJob(params: JobParameters): Boolean {
        stopped.set(false)
        // onStartJob 은 메인 스레드다. 확인은 블로킹 GET 이라 스레드로 넘긴다.
        Thread({
            try {
                runCheck(applicationContext, isStopped = stopped::get)
            } catch (e: Exception) {
                Log.w(TAG, "background update check failed", e) // 다음 주기에 다시 돈다 — 알릴 일이 아니다.
            } finally {
                jobFinished(params, false)
            }
        }, "cut-update-check").start()
        return true
    }

    override fun onStopJob(params: JobParameters): Boolean {
        stopped.set(true)
        return false // 주기 잡이라 다음 주기에 돈다.
    }

    internal companion object {
        private const val TAG = "CutUpdateJob"

        /**
         * Input : 컨텍스트, 중단 여부
         * Output: 없음(필요하면 알림 1건)
         * 핵심 로직: 알릴 버전이 있고 아직 알리지 않았으면 알리고 그 버전을 기억한다. 알림 권한이 없어 못 띄웠으면 기억하지
         *           않는다 — 나중에 권한을 주면 그때 알린다. GET 뒤에 설정·중단을 다시 본다: 그사이 사용자가 확인을 껐다면
         *           [UpdateCheckScheduler.sync] 가 이미 알림을 지웠고, 여기서 띄우면 그 선택을 되돌린다.
         */
        fun runCheck(context: Context, isStopped: () -> Boolean = { false }) {
            val preferences = UpdatePreferences(context)
            val manifest = UpdateRefresh.run(preferences) ?: return
            if (isStopped() || !preferences.isEnabled()) return
            if (!UpdateDecision.shouldNotify(manifest.versionCode, preferences.notifiedVersionCode())) return
            if (UpdateNotifier.show(context, manifest)) preferences.setNotifiedVersionCode(manifest.versionCode)
        }
    }
}

/** [UpdateCheckJobService] 예약을 "업데이트 확인" 설정에 맞춘다. */
internal object UpdateCheckScheduler {
    private const val JOB_ID = 0x0c_0228
    private val PERIOD_MILLIS = TimeUnit.HOURS.toMillis(12)
    private val FLEX_MILLIS = TimeUnit.HOURS.toMillis(1)

    /**
     * Input : 컨텍스트
     * Output: 없음
     * 핵심 로직: 켜져 있으면 예약하고(이미 있으면 그대로), 꺼져 있으면 취소한다. 앱이 열릴 때마다 부른다. 이미 있는 잡을 다시
     *           schedule 하지 않는 이유: 같은 ID 로 다시 예약하면 주기가 처음부터 다시 세어, 앱을 자주 여는 사람은 잡이 영영 돌지 않는다.
     */
    fun sync(context: Context) {
        val scheduler = context.getSystemService(JobScheduler::class.java) ?: return
        if (!UpdatePreferences(context).isEnabled()) {
            scheduler.cancel(JOB_ID)
            UpdateNotifier.cancel(context)
            return
        }
        if (scheduler.getPendingJob(JOB_ID) != null) return
        val job = JobInfo.Builder(JOB_ID, ComponentName(context, UpdateCheckJobService::class.java))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
            // flex 없이는 12시간 창 아무 데서나 돌아 두 실행 사이가 거의 24시간까지 벌어진다 — 창 끝 1시간으로 좁힌다.
            .setPeriodic(PERIOD_MILLIS, FLEX_MILLIS)
            .setPersisted(true) // 재부팅 뒤에도 유지(RECEIVE_BOOT_COMPLETED)
            .build()
        scheduler.schedule(job)
    }
}
