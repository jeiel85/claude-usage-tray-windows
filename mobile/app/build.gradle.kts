import com.android.build.api.variant.BuildConfigField
import java.util.Properties

plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// --- Version: one source of truth with the Windows release ---------------------------------
// CI passes -Pcut.version=<tag without v>. versionCode = major*10000 + minor*100 + patch so a
// newer tag always installs over an older one.
val cutVersion = providers.gradleProperty("cut.version").orElse("1.53.0").get()
val cutVersionCode = cutVersion.split('.', '-').take(3).map { it.toIntOrNull() ?: 0 }
    .let { (maj, min, pat) ->
        // The formula only round-trips while minor and patch stay below 100: StoreUpdateDecision.versionName turns
        // Play's versionCode back into "x.y.z" for the banner and notification (#288). Fail the build instead of
        // shipping a versionCode that reads back as a different version (or collides with another one).
        if (min !in 0..99 || pat !in 0..99) {
            throw GradleException("cut.version $cutVersion: minor and patch must be 0..99 for versionCode major*10000+minor*100+patch")
        }
        maj * 10000 + min * 100 + pat
    }

// --- Update manifest (#228) -----------------------------------------------------------------
// The in-app updater reads releases/latest/download/update.json — a fixed URL that redirects to the newest
// release, so it never changes between versions. Debug builds can point at a local server for an
// end-to-end check: -Pcut.updateManifestUrl=http://10.0.2.2:8000/update.json -Pcut.updateAllowHttp=true
val defaultManifestUrl = "https://github.com/jeiel85/claude-usage-tray-windows/releases/latest/download/update.json"
val debugManifestUrl = providers.gradleProperty("cut.updateManifestUrl").orElse(defaultManifestUrl).get()
val debugAllowHttp = providers.gradleProperty("cut.updateAllowHttp").orElse("false").get()

// --- Release signing --------------------------------------------------------------------------
// One key per distribution channel (#257). Values come from CI secrets (environment) or git-ignored files:
//   CUT_RELEASE_* (release-signing.properties) — the GitHub APK's cut key
//   CUT_PLAY_*    (play-signing.properties)    — the Google Play upload key
// Without them that channel's release build is unsigned (the release workflow then attaches a debug-signed APK).
val signingProps = Properties().apply {
    listOf("release-signing.properties", "play-signing.properties").forEach { name ->
        rootProject.file(name).takeIf { it.exists() }?.inputStream()?.use(::load)
    }
}
fun signing(name: String): String? = providers.environmentVariable(name).orNull ?: signingProps.getProperty(name)
fun hasSigning(prefix: String) =
    listOf("STORE_FILE", "STORE_PASSWORD", "KEY_ALIAS", "KEY_PASSWORD").all { !signing("${prefix}_$it").isNullOrBlank() }
val hasReleaseSigning = hasSigning("CUT_RELEASE")
val hasPlaySigning = hasSigning("CUT_PLAY")
if (providers.gradleProperty("cut.requireReleaseSigning").orNull == "true" && !hasReleaseSigning) {
    throw GradleException("Release signing values are missing (CUT_RELEASE_STORE_FILE/STORE_PASSWORD/KEY_ALIAS/KEY_PASSWORD).")
}
if (providers.gradleProperty("cut.requirePlaySigning").orNull == "true" && !hasPlaySigning) {
    throw GradleException("Play upload key values are missing (CUT_PLAY_STORE_FILE/STORE_PASSWORD/KEY_ALIAS/KEY_PASSWORD).")
}

android {
    namespace = "io.github.jeiel85.claudeusage"
    // Google Play requires targetSdk 36 (Android 16) for new uploads (#278). AGP 8.7.3 predates API 36,
    // so gradle.properties suppresses its compileSdk warning.
    compileSdk = 36

    defaultConfig {
        // com.jeiel85.<public app name> (#254). The Kotlin package and namespace stay io.github.jeiel85.claudeusage;
        // only the install identity changed, before the first Play upload fixes it for good.
        applicationId = "com.jeiel85.aiusage"
        minSdk = 26
        targetSdk = 36
        versionCode = cutVersionCode
        versionName = cutVersion
    }

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile = rootProject.file(signing("CUT_RELEASE_STORE_FILE")!!)
                storePassword = signing("CUT_RELEASE_STORE_PASSWORD")
                keyAlias = signing("CUT_RELEASE_KEY_ALIAS")
                keyPassword = signing("CUT_RELEASE_KEY_PASSWORD")
            }
        }
        if (hasPlaySigning) {
            create("playUpload") {
                storeFile = rootProject.file(signing("CUT_PLAY_STORE_FILE")!!)
                storePassword = signing("CUT_PLAY_STORE_PASSWORD")
                keyAlias = signing("CUT_PLAY_KEY_ALIAS")
                keyPassword = signing("CUT_PLAY_KEY_PASSWORD")
            }
        }
    }

    // --- Distribution channels (#257) ---------------------------------------------------------
    // github: the standalone APK on GitHub Releases, with the in-app updater (src/github: code, permissions,
    //         update.json), signed with the cut key above.
    // play:   the Google Play bundle. Play installs updates and its policy forbids self-updating, so no updater.
    //         Signed with the Play upload key, not the cut key (one key per channel); Google re-signs it with
    //         the app signing key it keeps (Play App Signing).
    // Same applicationId and versionCode; the signatures differ, so one channel cannot update the other.
    flavorDimensions += "channel"
    productFlavors {
        create("github") {
            dimension = "channel"
            buildConfigField("String", "UPDATE_MANIFEST_URL", "\"$defaultManifestUrl\"")
            buildConfigField("boolean", "UPDATE_ALLOW_HTTP", "false")
            // Release builds only: the debug build type keeps its own debug signing config.
            if (hasReleaseSigning) signingConfig = signingConfigs.getByName("release")
        }
        create("play") {
            dimension = "channel"
            if (hasPlaySigning) signingConfig = signingConfigs.getByName("playUpload")
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures {
        compose = true
        buildConfig = true
    }
    dependenciesInfo {
        includeInApk = false
        includeInBundle = false
    }
    packaging {
        resources.excludes += "/META-INF/{AL2.0,LGPL2.1}"
    }
}

// The GitHub debug build can point its updater at a local server (see the update manifest note above).
androidComponents {
    onVariants(selector().withFlavor("channel" to "github").withBuildType("debug")) { variant ->
        variant.buildConfigFields.put("UPDATE_MANIFEST_URL", BuildConfigField("String", "\"$debugManifestUrl\"", null))
        variant.buildConfigFields.put("UPDATE_ALLOW_HTTP", BuildConfigField("boolean", debugAllowHttp, null))
    }
}

dependencies {
    val composeBom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(composeBom)
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material:material-icons-core")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.lifecycle:lifecycle-runtime-compose:2.8.7")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
    implementation("com.journeyapps:zxing-android-embedded:4.3.0")
    implementation("com.google.zxing:core:3.5.3")
    // Play build only (#284): asks the Google Play app whether a newer version is out for this user. Play installs it.
    "playImplementation"("com.google.android.play:app-update:2.1.0")

    testImplementation("junit:junit:4.13.2")
    // Real org.json on the JVM test classpath (android.jar only ships stubs).
    testImplementation("org.json:json:20240303")
    // A local HTTP server for UsageApi calls (same version as okhttp above).
    testImplementation("com.squareup.okhttp3:mockwebserver:4.12.0")
}
