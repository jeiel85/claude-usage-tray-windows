package io.github.jeiel85.claudeusage.update

import android.content.Context

/** What the Play build's update notice remembers (#284). SharedPreferences: read outside coroutines (composition, job). */
internal class StoreUpdatePreferences(context: Context) {

    private val prefs = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** Background check and notification. On by default; the in-app banner shows either way (it asks only the phone's Play app). */
    fun isNotifyEnabled(): Boolean = prefs.getBoolean(KEY_NOTIFY, true)

    fun setNotifyEnabled(enabled: Boolean) = prefs.edit().putBoolean(KEY_NOTIFY, enabled).apply()

    /** The versionCode the background check already notified about, or 0 ([StoreUpdateDecision.shouldNotify]). */
    fun notifiedVersionCode(): Int = prefs.getInt(KEY_NOTIFIED_VERSION_CODE, 0)

    fun setNotifiedVersionCode(versionCode: Int) = prefs.edit().putInt(KEY_NOTIFIED_VERSION_CODE, versionCode).apply()

    private companion object {
        const val NAME = "cut_store_update"
        const val KEY_NOTIFY = "notify"
        const val KEY_NOTIFIED_VERSION_CODE = "notified_version_code"
    }
}
