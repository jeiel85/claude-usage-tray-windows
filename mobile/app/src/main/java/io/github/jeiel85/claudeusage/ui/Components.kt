package io.github.jeiel85.claudeusage.ui

import androidx.compose.animation.animateColorAsState
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.selected
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

internal val CardShape = RoundedCornerShape(18.dp)

/** The one card style every screen uses: a dark fill with a hairline border, so cards read as layers, not slabs. */
@Composable
internal fun Card(modifier: Modifier = Modifier, onClick: (() -> Unit)? = null, content: @Composable () -> Unit) {
    Box(
        modifier
            .fillMaxWidth()
            .clip(CardShape)
            .background(Palette.Card)
            .border(1.dp, Palette.CardBorder, CardShape)
            .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier)
            .padding(16.dp),
    ) { content() }
}

/** Small rounded label (plan name, "NEW", connection state). */
@Composable
internal fun Chip(text: String, color: Color, background: Color = color.copy(alpha = 0.14f), dot: Boolean = false) {
    Row(
        Modifier.clip(RoundedCornerShape(50)).background(background).padding(horizontal = 8.dp, vertical = 3.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (dot) {
            Box(Modifier.size(6.dp).clip(CircleShape).background(color))
            Spacer(Modifier.width(5.dp))
        }
        Text(text, color = color, fontSize = 11.sp, fontWeight = FontWeight.SemiBold, maxLines = 1)
    }
}

/** A service's initial in its colour — the card's anchor, recognisable before the name is read. */
@Composable
internal fun Monogram(letter: String, color: Color) {
    Box(
        Modifier.size(34.dp).clip(RoundedCornerShape(10.dp)).background(color.copy(alpha = 0.16f)),
        contentAlignment = Alignment.Center,
    ) {
        Text(letter, color = color, fontSize = 16.sp, fontWeight = FontWeight.Bold)
    }
}

/**
 * Two-way switch between home pages, drawn as a pill instead of a full-width tab bar: lighter, and it says
 * "two views of the same data". The pager still moves on a sideways swipe; [selected] follows it.
 */
@Composable
internal fun SegmentedTabs(titles: List<String>, selected: Int, onSelect: (Int) -> Unit, modifier: Modifier = Modifier) {
    Row(
        modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(50))
            .background(Palette.Card)
            .border(1.dp, Palette.CardBorder, RoundedCornerShape(50))
            .padding(4.dp),
        horizontalArrangement = Arrangement.spacedBy(4.dp),
    ) {
        titles.forEachIndexed { index, title ->
            val isSelected = index == selected
            val bg by animateColorAsState(if (isSelected) Palette.Raised else Color.Transparent, label = "tabBg")
            val fg by animateColorAsState(if (isSelected) Palette.Text else Palette.TextSoft, label = "tabFg")
            Box(
                Modifier
                    .weight(1f)
                    .height(36.dp)
                    .clip(RoundedCornerShape(50))
                    .background(bg)
                    .semantics { this.selected = isSelected }
                    .clickable(role = Role.Tab) { onSelect(index) },
                contentAlignment = Alignment.Center,
            ) {
                Text(title, color = fg, fontSize = 14.sp, fontWeight = FontWeight.SemiBold, textAlign = TextAlign.Center, maxLines = 1)
            }
        }
    }
}

/** Section title above a group of cards (settings, home). */
@Composable
internal fun SectionHeader(text: String, trailing: String? = null) {
    Row(Modifier.fillMaxWidth().padding(start = 4.dp, end = 4.dp, top = 12.dp, bottom = 2.dp), verticalAlignment = Alignment.CenterVertically) {
        Text(text, color = Palette.AccentLight, fontSize = 13.sp, fontWeight = FontWeight.SemiBold, modifier = Modifier.weight(1f))
        trailing?.let { Text(it, color = Palette.TextDim, fontSize = 12.sp) }
    }
}

/** The app's switch: a little smaller than Material's default, which dominated a card holding a column of them. */
@Composable
internal fun AppSwitch(checked: Boolean, onCheckedChange: (Boolean) -> Unit, enabled: Boolean = true) {
    Switch(
        checked = checked,
        onCheckedChange = onCheckedChange,
        enabled = enabled,
        modifier = Modifier.scale(0.85f),
        colors = SwitchDefaults.colors(
            checkedTrackColor = Palette.Accent,
            uncheckedTrackColor = Palette.Track,
            uncheckedBorderColor = Palette.Line,
            uncheckedThumbColor = Palette.TextSoft,
        ),
    )
}
