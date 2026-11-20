package io.github.jeiel85.claudeusage.update

import com.google.android.play.core.install.model.UpdateAvailability
import java.util.concurrent.TimeUnit

/** The Play build's update rules (#284), kept free of Android and Play calls so tests can pin them. */
internal object StoreUpdateDecision {

    /** Coming back to the app re-asks Play only after this long. Play itself refreshes from Google on its own schedule. */
    val RECHECK_MILLIS: Long = TimeUnit.HOURS.toMillis(6)

    fun isStale(lastCheckedAt: Long, now: Long): Boolean =
        lastCheckedAt <= 0L || lastCheckedAt > now || now - lastCheckedAt >= RECHECK_MILLIS

    /**
     * Input : Play's `updateAvailability()` and `availableVersionCode()`, this build's versionCode
     * Output: [StoreUpdates.Result.Available] only when Play offers a strictly higher versionCode, else UpToDate
     * 핵심 로직: an update Play is already installing (DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS) is not offered again.
     *           The versionCode check guards against a local build newer than the store's.
     */
    fun interpret(updateAvailability: Int, availableVersionCode: Int, installedVersionCode: Int): StoreUpdates.Result =
        if (updateAvailability == UpdateAvailability.UPDATE_AVAILABLE && availableVersionCode > installedVersionCode) {
            StoreUpdates.Result.Available(availableVersionCode)
        } else {
            StoreUpdates.Result.UpToDate
        }

    /** One notification per version: the background check runs twice a day and would repeat it otherwise. */
    fun shouldNotify(availableVersionCode: Int, notifiedVersionCode: Int): Boolean = availableVersionCode != notifiedVersionCode

    /**
     * versionCode (major*10000 + minor*100 + patch, app/build.gradle.kts) back to "1.67.0". Play reports only the
     * versionCode of the version it offers.
     */
    fun versionName(versionCode: Int): String = "${versionCode / 10000}.${versionCode / 100 % 100}.${versionCode % 100}"
}
