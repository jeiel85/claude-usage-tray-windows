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
    .let { (maj, min, pat) -> maj * 10000 + min * 100 + pat }

// --- Update manifest (#228) -----------------------------------------------------------------
// The in-app updater reads releases/latest/download/update.json — a fixed URL that redirects to the newest
// release, so it never changes between versions. Debug builds can point at a local server for an
// end-to-end check: -Pcut.updateManifestUrl=http://10.0.2.2:8000/update.json -Pcut.updateAllowHttp=true
val defaultManifestUrl = "https://github.com/jeiel85/claude-usage-tray-windows/releases/latest/download/update.json"
val debugManifestUrl = providers.gradleProperty("cut.updateManifestUrl").orElse(defaultManifestUrl).get()
val debugAllowHttp = providers.gradleProperty("cut.updateAllowHttp").orElse("false").get()

// --- Release signing --------------------------------------------------------------------------
// Values come from CI secrets (environment) or a git-ignored release-signing.properties. Without
// them the release build is unsigned and the release workflow skips the APK.
val signingProps = Properties().apply {
    rootProject.file("release-signing.properties").takeIf { it.exists() }?.inputStream()?.use(::load)
}
fun signing(name: String): String? = providers.environmentVariable(name).orNull ?: signingProps.getProperty(name)
val storeFilePath = signing("CUT_RELEASE_STORE_FILE")
val hasReleaseSigning = listOf(
    storeFilePath,
    signing("CUT_RELEASE_STORE_PASSWORD"),
    signing("CUT_RELEASE_KEY_ALIAS"),
    signing("CUT_RELEASE_KEY_PASSWORD"),
).all { !it.isNullOrBlank() }
if (providers.gradleProperty("cut.requireReleaseSigning").orNull == "true" && !hasReleaseSigning) {
    throw GradleException("Release signing values are missing (CUT_RELEASE_STORE_FILE/STORE_PASSWORD/KEY_ALIAS/KEY_PASSWORD).")
}

android {
    namespace = "io.github.jeiel85.claudeusage"
    compileSdk = 35

    defaultConfig {
        // com.jeiel85.<public app name> (#254). The Kotlin package and namespace stay io.github.jeiel85.claudeusage;
        // only the install identity changed, before the first Play upload fixes it for good.
        applicationId = "com.jeiel85.aiusage"
        minSdk = 26
        targetSdk = 35
        versionCode = cutVersionCode
        versionName = cutVersion
    }

    signingConfigs {
        if (hasReleaseSigning) {
            create("release") {
                storeFile = rootProject.file(storeFilePath!!)
                storePassword = signing("CUT_RELEASE_STORE_PASSWORD")
                keyAlias = signing("CUT_RELEASE_KEY_ALIAS")
                keyPassword = signing("CUT_RELEASE_KEY_PASSWORD")
            }
        }
    }

    // --- Distribution channels (#257) ---------------------------------------------------------
    // github: the standalone APK on GitHub Releases, with the in-app updater (src/github: code, permissions,
    //         update.json), signed with the cut key above.
    // play:   the Google Play bundle. Play installs updates and its policy forbids self-updating, so no updater.
    //         Signed with a separate Play upload key, not the cut key (one key per channel); none is set up yet,
    //         so the Play release bundle is unsigned for now.
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

    testImplementation("junit:junit:4.13.2")
    // Real org.json on the JVM test classpath (android.jar only ships stubs).
    testImplementation("org.json:json:20240303")
}
