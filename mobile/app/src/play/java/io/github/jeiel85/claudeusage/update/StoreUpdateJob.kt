package io.github.jeiel85.claudeusage.update

import android.app.job.JobInfo
import android.app.job.JobParameters
import android.app.job.JobScheduler
import android.app.job.JobService
import android.content.ComponentName
import android.content.Context
import android.util.Log
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import java.util.concurrent.TimeUnit

/**
 * Background "is there a new Play version?" check (#284), so a user who turned Play's auto-update off still hears
 * about it without opening the app. Platform JobScheduler like the GitHub build's check — no WorkManager for one job.
 * Twice a day with a network; one notification per version.
 */
class StoreUpdateJobService : JobService() {

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var running: Job? = null

    override fun onStartJob(params: JobParameters): Boolean {
        running = scope.launch {
            try {
                runCheck(applicationContext)
            } catch (e: Exception) {
                if (e is kotlinx.coroutines.CancellationException) throw e
                Log.w(TAG, "background store update check failed", e) // the next period tries again
            } finally {
                jobFinished(params, false)
            }
        }
        return true
    }

    override fun onStopJob(params: JobParameters): Boolean {
        running?.cancel()
        return false // periodic: runs again next period
    }

    override fun onDestroy() {
        scope.cancel()
        super.onDestroy()
    }

    internal companion object {
        private const val TAG = "CutStoreUpdateJob"

        /**
         * Input : context
         * Output: none (at most one notification)
         * 핵심 로직: the setting is read again after the check — if the user turned notifications off meanwhile,
         *           [StoreUpdateScheduler.sync] already cleared the notice and this must not bring it back.
         */
        suspend fun runCheck(context: Context) {
            val preferences = StoreUpdatePreferences(context)
            if (!preferences.isNotifyEnabled()) return
            val result = StoreUpdates.check(context)
            if (!preferences.isNotifyEnabled()) return
            when (result) {
                is StoreUpdates.Result.Available -> {
                    if (!StoreUpdateDecision.shouldNotify(result.versionCode, preferences.notifiedVersionCode())) return
                    if (StoreUpdateNotifier.show(context, result.versionCode)) preferences.setNotifiedVersionCode(result.versionCode)
                }
                StoreUpdates.Result.UpToDate -> StoreUpdateNotifier.cancel(context)
                StoreUpdates.Result.NotFromPlay, StoreUpdates.Result.Failed -> Unit
            }
        }
    }
}

/** Keeps [StoreUpdateJobService] scheduled to match the "new version notifications" setting. */
internal object StoreUpdateScheduler {
    // Not the GitHub build's 0x0c_0228: one build never has both, but a distinct ID keeps adb commands unambiguous.
    const val JOB_ID = 0x0c_0284
    private val PERIOD_MILLIS = TimeUnit.HOURS.toMillis(12)
    private val FLEX_MILLIS = TimeUnit.HOURS.toMillis(1)

    /**
     * Schedules when on (leaving an existing job alone — re-scheduling the same ID restarts its period, so someone who
     * opens the app often would never get a run), cancels and clears the notice when off. Called on every app start.
     */
    fun sync(context: Context) {
        val scheduler = context.getSystemService(JobScheduler::class.java) ?: return
        if (!StoreUpdatePreferences(context).isNotifyEnabled()) {
            scheduler.cancel(JOB_ID)
            StoreUpdateNotifier.cancel(context)
            return
        }
        if (scheduler.getPendingJob(JOB_ID) != null) return
        val job = JobInfo.Builder(JOB_ID, ComponentName(context, StoreUpdateJobService::class.java))
            .setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY)
            .setPeriodic(PERIOD_MILLIS, FLEX_MILLIS)
            .setPersisted(true) // survives reboots (RECEIVE_BOOT_COMPLETED)
            .build()
        scheduler.schedule(job)
    }
}
