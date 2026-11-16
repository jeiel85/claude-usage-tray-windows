package io.github.jeiel85.claudeusage.update

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.util.concurrent.TimeUnit

/**
 * The updater's rules that would annoy most if wrong (#228): when to check, what to offer, what to trust.
 * The manifest shape is the one release.yml writes (same keys as Homebase's update.json).
 */
class UpdateLogicTest {

    private val sha = "a".repeat(64)

    private fun json(
        versionCode: Any = 15500,
        apkUrl: String = "https://github.com/jeiel85/claude-usage-tray-windows/releases/download/v1.55.0/ClaudeUsageTray-Mobile.apk",
        sha256: String = sha,
        minSdk: Int = 26,
        notes: String = "https://github.com/jeiel85/claude-usage-tray-windows/releases/tag/v1.55.0",
    ) = """{"versionCode":$versionCode,"versionName":"1.55.0","apkUrl":"$apkUrl","apkSizeBytes":9873837,
        |"sha256":"$sha256","minSdk":$minSdk,"releaseNotesUrl":"$notes"}""".trimMargin()

    private fun manifest(versionCode: Int = 15500, minSdk: Int = 26) =
        UpdateManifestParser.parse(json(versionCode = versionCode, minSdk = minSdk), allowHttp = false)!!

    @Test
    fun parses_the_release_manifest() {
        val m = UpdateManifestParser.parse(json(), allowHttp = false)
        assertNotNull(m)
        assertEquals(15500, m!!.versionCode)
        assertEquals(9873837L, m.apkSizeBytes)
    }

    @Test
    fun anything_suspicious_is_null() {
        assertNull(UpdateManifestParser.parse("not json", allowHttp = false))
        assertNull(UpdateManifestParser.parse("{}", allowHttp = false))
        assertNull(UpdateManifestParser.parse(json(versionCode = -1), allowHttp = false))
        assertNull(UpdateManifestParser.parse(json(versionCode = "\"x\""), allowHttp = false))
        assertNull(UpdateManifestParser.parse(json(sha256 = "short"), allowHttp = false))
        // Plain http could let a man in the middle swap the APK address; only debug verification builds allow it.
        val http = json(apkUrl = "http://10.0.2.2:8000/app.apk", notes = "http://10.0.2.2:8000/notes")
        assertNull(UpdateManifestParser.parse(http, allowHttp = false))
        assertNotNull(UpdateManifestParser.parse(http, allowHttp = true))
    }

    @Test
    fun checks_once_a_day_and_survives_a_clock_that_jumped_back() {
        val day = TimeUnit.DAYS.toMillis(1)
        val now = 10 * day
        assertTrue(UpdateDecision.shouldCheckNow(0L, now))
        assertFalse(UpdateDecision.shouldCheckNow(now - day + 1, now))
        assertTrue(UpdateDecision.shouldCheckNow(now - day, now))
        assertTrue(UpdateDecision.shouldCheckNow(now + day, now)) // last check "in the future" must not block forever
    }

    @Test
    fun offers_only_a_newer_installable_unskipped_version() {
        assertTrue(UpdateDecision.shouldOffer(manifest(15500), installedVersionCode = 15400, skippedVersionCode = 0, deviceSdk = 34))
        assertFalse(UpdateDecision.shouldOffer(manifest(15400), 15400, 0, 34)) // same
        assertFalse(UpdateDecision.shouldOffer(manifest(15300), 15400, 0, 34)) // older (dev build)
        assertFalse(UpdateDecision.shouldOffer(manifest(15500), 15400, 15500, 34)) // skipped
        assertFalse(UpdateDecision.shouldOffer(manifest(15500, minSdk = 35), 15400, 0, 34)) // would fail to install
        // Integer comparison: 1.10.0 (11000) is newer than 1.9.0 (10900), unlike a string compare.
        assertTrue(UpdateDecision.shouldOffer(manifest(11000), 10900, 0, 34))
    }

    @Test
    fun notifies_once_per_version() {
        assertTrue(UpdateDecision.shouldNotify(15500, 0))
        assertFalse(UpdateDecision.shouldNotify(15500, 15500))
        assertTrue(UpdateDecision.shouldNotify(15600, 15500))
    }
}
