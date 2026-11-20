package io.github.jeiel85.claudeusage.data

/**
 * PC names on one line (#271): the name a PC sends and the alias typed on this phone. Same rules as the PC's
 * `PcName.Clean` — trimmed, whitespace and control characters folded into single spaces, cut without
 * splitting an emoji — so a name shows the same on every screen and never breaks a notification line.
 */
object PcNames {
    /** Longest alias the rename dialog accepts; the PC's own setting has the same limit. */
    const val MAX_ALIAS = 32

    /** Longest name accepted from a PC. */
    const val MAX_RECEIVED = 64

    /** null for a blank name. */
    fun clean(name: String?, maxLength: Int = MAX_RECEIVED): String? {
        if (name.isNullOrBlank()) return null
        val sb = StringBuilder(name.length)
        var pendingSpace = false
        for (ch in name) {
            if (ch.isWhitespace() || ch.isISOControl()) {
                pendingSpace = sb.isNotEmpty()
                continue
            }
            if (pendingSpace) sb.append(' ')
            pendingSpace = false
            sb.append(ch)
        }
        if (sb.length <= maxLength) return sb.toString()
        var cut = maxLength
        if (Character.isHighSurrogate(sb[cut - 1])) cut--
        return sb.substring(0, cut).trimEnd()
    }
}
