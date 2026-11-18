package io.github.jeiel85.claudeusage.update

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.util.Log
import com.google.android.play.core.appupdate.AppUpdateInfo
import com.google.android.play.core.appupdate.AppUpdateManager
import com.google.android.play.core.appupdate.AppUpdateManagerFactory
import com.google.android.play.core.appupdate.testing.FakeAppUpdateManager
import com.google.android.play.core.install.InstallException
import com.google.android.play.core.install.model.InstallErrorCode
import io.github.jeiel85.claudeusage.BuildConfig
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withTimeoutOrNull
import kotlin.coroutines.resume

/**
 * "Is there a newer version on Google Play?" for the Play build (#284, the Doran approach). The Play In-App Updates API
 * asks the Google Play app on this phone, which knows every track open to this user (internal and closed testing
 * included). The answer only drives a banner, a settings row and one notification per version that open the Play
 * listing — Play does the installing; this build never downloads or installs anything itself.
 */
internal object StoreUpdates {

    private const val TAG = "CutStoreUpdate"
    private const val TIMEOUT_MILLIS = 15_000L

    /** What one check found. */
    sealed interface Result {
        data class Available(val versionCode: Int) : Result
        data object UpToDate : Result

        /** Play can't answer for this copy: not installed from Play (sideloaded/debug), or no Play Store. */
        data object NotFromPlay : Result

        /** Play didn't answer (timeout, Play Store busy or offline). */
        data object Failed : Result
    }

    private val _available = MutableStateFlow<Int?>(null)

    /** The newer versionCode Play offers, or null. The home banner and the settings card both read it. */
    val available: StateFlow<Int?> = _available.asStateFlow()

    @Volatile private var lastCheckedAt = 0L

    /** Debug builds only ([useFakeUpdate]): the versionCode to pretend Play offers, or 0. */
    @Volatile private var fakeVersionCode = 0

    /**
     * Input : context
     * Output: [Result]; [available] follows it (only a definite answer clears it — a failed check keeps what we knew)
     * 핵심 로직: one binder call to the Play Store app, bounded by a timeout so a stuck Play Store can't hang the
     *           background job. Any main or background thread.
     */
    suspend fun check(context: Context): Result {
        val result = withTimeoutOrNull(TIMEOUT_MILLIS) { query(context.applicationContext) } ?: Result.Failed
        when (result) {
            is Result.Available -> _available.value = result.versionCode
            Result.UpToDate, Result.NotFromPlay -> _available.value = null
            Result.Failed -> Unit
        }
        if (result != Result.Failed) lastCheckedAt = System.currentTimeMillis()
        return result
    }

    /** Coming back to the app: check again only when the last answer is older than [StoreUpdateDecision.RECHECK_MILLIS]. */
    suspend fun checkIfStale(context: Context) {
        if (StoreUpdateDecision.isStale(lastCheckedAt, System.currentTimeMillis())) check(context)
    }

    private suspend fun query(context: Context): Result = try {
        val info = manager(context).requestInfo()
        StoreUpdateDecision.interpret(
            updateAvailability = info.updateAvailability(),
            availableVersionCode = info.availableVersionCode(),
            installedVersionCode = BuildConfig.VERSION_CODE,
        )
    } catch (e: InstallException) {
        Log.i(TAG, "Play can't check this copy: ${e.errorCode}")
        if (e.errorCode in NOT_FROM_PLAY) Result.NotFromPlay else Result.Failed
    } catch (e: CancellationException) {
        throw e // the timeout above, or the screen went away
    } catch (e: Exception) {
        Log.w(TAG, "update check failed", e)
        Result.Failed
    }

    private val NOT_FROM_PLAY = setOf(
        InstallErrorCode.ERROR_APP_NOT_OWNED,
        InstallErrorCode.ERROR_API_NOT_AVAILABLE,
        InstallErrorCode.ERROR_PLAY_STORE_NOT_FOUND,
    )

    private fun manager(context: Context): AppUpdateManager =
        if (BuildConfig.DEBUG && fakeVersionCode > 0) {
            FakeAppUpdateManager(context).apply { setUpdateAvailable(fakeVersionCode) }
        } else {
            AppUpdateManagerFactory.create(context)
        }

    private suspend fun AppUpdateManager.requestInfo(): AppUpdateInfo = suspendCancellableCoroutine { cont ->
        appUpdateInfo
            .addOnSuccessListener { cont.resume(it) }
            .addOnFailureListener { e ->
                // Rethrown by query(): InstallException tells "not from Play" apart from a passing failure.
                cont.resumeWith(kotlin.Result.failure(e))
            }
    }

    /** Debug builds only ([AppUpdates]): pretend Play offers [versionCode]. Lasts for this process. */
    fun useFakeUpdate(versionCode: Int) {
        if (!BuildConfig.DEBUG) return
        fakeVersionCode = versionCode
        lastCheckedAt = 0L
    }

    /**
     * The Play Store app's page for this app, or the web listing when there is no Play Store app. Testers see their
     * track's newer version there.
     */
    fun openStore(context: Context) {
        try {
            context.startActivity(storeAppIntent(context.packageName).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (_: ActivityNotFoundException) {
            runCatching {
                context.startActivity(
                    Intent(Intent.ACTION_VIEW, Uri.parse("https://play.google.com/store/apps/details?id=${context.packageName}"))
                        .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                )
            }
        }
    }

    private fun storeAppIntent(packageName: String): Intent =
        Intent(Intent.ACTION_VIEW, Uri.parse("market://details?id=$packageName")).setPackage("com.android.vending")
}
