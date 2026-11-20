package io.github.jeiel85.claudeusage.push

import android.content.Context
import io.github.jeiel85.claudeusage.data.KeystoreCipher
import java.security.KeyFactory
import java.security.SecureRandom
import java.security.interfaces.ECPrivateKey
import java.security.spec.PKCS8EncodedKeySpec

/** This phone's push identity: its P-256 key pair and 16-byte auth secret (RFC 8291 §2). */
class PushKeys(val privateKey: ECPrivateKey, val publicKey: ByteArray, val authSecret: ByteArray) {
    val p256dh: String get() = WebPush.b64url(publicKey)
    val auth: String get() = WebPush.b64url(authSecret)
}

/**
 * UnifiedPush registration state and keys in SharedPreferences.
 *
 * The key pair is generated in software and its private key (PKCS#8) and the auth secret are stored
 * encrypted with a [KeystoreCipher] key that never leaves the Android Keystore. Keystore-native ECDH
 * (`PURPOSE_AGREE_KEY`) needs API 31 and the app supports API 26, so one code path wraps instead.
 * One key pair and one endpoint serve every paired PC; the message names its PC.
 */
class PushState(context: Context) {

    private val prefs = context.applicationContext.getSharedPreferences(NAME, Context.MODE_PRIVATE)
    private val cipher = KeystoreCipher(KEY_ALIAS)

    /** Connection token of the current registration request (AND_3 `token`); null when none. */
    val token: String? get() = prefs.getString(KEY_TOKEN, null)

    /** Package of the distributor app we registered with. */
    val distributor: String? get() = prefs.getString(KEY_DISTRIBUTOR, null)

    /** Endpoint URL the distributor gave us; null until NEW_ENDPOINT arrives. */
    val endpoint: String? get() = prefs.getString(KEY_ENDPOINT, null)

    /** Set when the user turned push off; suppresses the automatic first registration. */
    var userDisabled: Boolean
        get() = prefs.getBoolean(KEY_DISABLED, false)
        set(value) = prefs.edit().putBoolean(KEY_DISABLED, value).apply()

    /** Last problem worth showing (registration failed, distributor gone…). */
    var lastError: String?
        get() = prefs.getString(KEY_ERROR, null)
        set(value) = prefs.edit().putString(KEY_ERROR, value).apply()

    /** Notification categories the user turned off (Homebase M-25); sent with the subscription, and dropped here too. */
    var mutedCategories: Set<String>
        get() = prefs.getStringSet(KEY_MUTED, emptySet())?.toSet() ?: emptySet()
        set(value) = prefs.edit().putStringSet(KEY_MUTED, value.toSet()).apply()

    /** Recently shown alerts (see [EventDedupe]); JSON kept small and pruned on every write. */
    var recentEvents: String?
        get() = prefs.getString(KEY_EVENTS, null)
        set(value) = prefs.edit().putString(KEY_EVENTS, value).apply()

    /** PCs that accepted the current endpoint. */
    val registeredPcs: Set<String> get() = prefs.getStringSet(KEY_REGISTERED, emptySet()) ?: emptySet()

    fun startRegistration(token: String, distributor: String) = synchronized(LOCK) {
        prefs.edit()
            .putString(KEY_TOKEN, token)
            .putString(KEY_DISTRIBUTOR, distributor)
            .remove(KEY_ENDPOINT)
            .remove(KEY_ERROR)
            .putStringSet(KEY_REGISTERED, emptySet())
            .apply()
    }

    /** Returns true when the endpoint changed (every PC must then be told). */
    fun setEndpoint(endpoint: String): Boolean = synchronized(LOCK) {
        if (endpoint == this.endpoint) return false
        prefs.edit().putString(KEY_ENDPOINT, endpoint).remove(KEY_ERROR).putStringSet(KEY_REGISTERED, emptySet()).apply()
        return true
    }

    fun clear() = synchronized(LOCK) {
        prefs.edit().remove(KEY_TOKEN).remove(KEY_DISTRIBUTOR).remove(KEY_ENDPOINT).putStringSet(KEY_REGISTERED, emptySet()).apply()
    }

    fun markRegistered(pcId: String, registered: Boolean) = synchronized(LOCK) {
        val set = registeredPcs.toMutableSet()
        if (registered) set += pcId else set -= pcId
        prefs.edit().putStringSet(KEY_REGISTERED, set).apply()
    }

    /** The subscription to give each PC, or null while there is no endpoint. */
    fun subscription(): PushSubscription? {
        val endpoint = endpoint ?: return null
        val keys = keys()
        return PushSubscription(endpoint, keys.p256dh, keys.auth, mutedCategories.sorted())
    }

    /**
     * The phone's keys, created on first use. If the Keystore key was lost (data restored on another
     * phone) the old keys can't be read; new ones are made and the next registration tells every PC.
     */
    fun keys(): PushKeys = synchronized(LOCK) {
        val pkcs8 = cipher.decrypt(prefs.getString(KEY_PRIVATE, null))
        val auth = cipher.decrypt(prefs.getString(KEY_AUTH, null))
        val pub = prefs.getString(KEY_PUBLIC, null)?.let { runCatching { WebPush.unb64url(it) }.getOrNull() }
        if (pkcs8 != null && auth != null && pub != null) {
            runCatching {
                val key = KeyFactory.getInstance("EC").generatePrivate(PKCS8EncodedKeySpec(pkcs8)) as ECPrivateKey
                return PushKeys(key, pub, auth)
            }
        }
        val pair = WebPush.generateKeyPair()
        val secret = ByteArray(16).also { SecureRandom().nextBytes(it) }
        val publicBytes = WebPush.uncompressed(pair.public as java.security.interfaces.ECPublicKey)
        prefs.edit()
            .putString(KEY_PRIVATE, cipher.encrypt(pair.private.encoded))
            .putString(KEY_AUTH, cipher.encrypt(secret))
            .putString(KEY_PUBLIC, WebPush.b64url(publicBytes))
            .putStringSet(KEY_REGISTERED, emptySet())
            .apply()
        return PushKeys(pair.private as ECPrivateKey, publicBytes, secret)
    }

    private companion object {
        /**
         * Process-wide: every caller makes its own PushState, and concurrent registrations with several
         * PCs must not each generate a key pair (the PCs would then hold different identities).
         */
        val LOCK = Any()
        const val NAME = "cut_push"
        const val KEY_ALIAS = "cut_push_keys"
        const val KEY_TOKEN = "token"
        const val KEY_DISTRIBUTOR = "distributor"
        const val KEY_ENDPOINT = "endpoint"
        const val KEY_DISABLED = "userDisabled"
        const val KEY_ERROR = "lastError"
        const val KEY_REGISTERED = "registeredPcs"
        const val KEY_MUTED = "mutedCategories"
        const val KEY_PRIVATE = "privateKey"
        const val KEY_PUBLIC = "publicKey"
        const val KEY_AUTH = "authSecret"
        const val KEY_EVENTS = "recentEvents"
    }
}
