package io.github.jeiel85.claudeusage.net

import java.util.Base64

/**
 * The pairing QR as a link, mirroring the PC's `PairingLink`:
 *
 * - `https://jeiel85.github.io/claude-usage-tray-windows/pair/#<base64url(offer JSON)>` — what the PC
 *   shows. The phone's camera opens it in the app or, without the app, on the install guide page.
 * - `claudeusage://pair/<base64url(offer JSON)>` — what that page's "open in app" button hands over.
 *
 * The offer rides in the fragment / custom-scheme path, so the one-time code never reaches a web server.
 */
object PairingLink {
    const val WEB = "https://jeiel85.github.io/claude-usage-tray-windows/pair"
    const val APP = "claudeusage://pair/"

    /** The base64url payload of a pairing link, or null when [text] isn't one. */
    fun payload(text: String): String? {
        val t = text.trim()
        val raw = when {
            t.startsWith(WEB) -> t.substringAfter('#', "")
            t.startsWith(APP) -> t.removePrefix(APP).substringBefore('?').substringBefore('#')
            else -> return null
        }
        return raw.trim().ifEmpty { null }
    }

    /** The offer JSON inside a pairing link, or null. */
    fun decode(text: String): String? = payload(text)?.let {
        try {
            String(Base64.getUrlDecoder().decode(it.trimEnd('=')), Charsets.UTF_8)
        } catch (_: IllegalArgumentException) {
            null
        }
    }

    fun isLink(text: String?): Boolean = text != null && payload(text) != null
}
