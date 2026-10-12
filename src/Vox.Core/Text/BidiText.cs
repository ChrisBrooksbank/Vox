using System.Globalization;
using Vox.Core.Braille;

namespace Vox.Core.Text;

/// <summary>
/// Right-to-left text (Arabic, Hebrew). Vox keeps text in logical order everywhere — the buffer,
/// the cursor, speech and braille — as NVDA does: speech needs the reading order, and Arabic and
/// Hebrew braille are read left to right in reading order too, so nothing is reversed. What
/// needs care are the invisible direction marks pages and editors put in mixed-direction text
/// (U+200E, U+200F, U+061C, embeddings and isolates): they would show as junk braille cells and
/// stop the cursor on nothing, so braille leaves them out while keeping the cursor and routing on
/// the right characters.
/// </summary>
public static class BidiText
{
    /// <summary>Whether <paramref name="c"/> is an invisible bidirectional formatting character.</summary>
    public static bool IsControl(char c) =>
        c is '‎' or '‏' or '؜' or (>= '‪' and <= '‮') or (>= '⁦' and <= '⁩');

    /// <summary>The direction of a paragraph: that of its first strong letter (Unicode's rule P2); left to right without one.</summary>
    public static bool IsRightToLeft(string text)
    {
        foreach (var c in text)
        {
            if (IsStrongRightToLeft(c))
                return true;
            if (char.IsLetter(c))
                return false;
        }
        return false;
    }

    /// <summary>Hebrew, Arabic, Syriac, Thaana, N'Ko and their presentation forms.</summary>
    public static bool IsStrongRightToLeft(char c) =>
        c is (>= '֐' and <= 'ࣿ') or (>= 'יִ' and <= '﷿') or (>= 'ﹰ' and <= '﻿')
        && CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.OtherLetter or UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter;

    /// <summary>
    /// <paramref name="text"/> without its direction marks, and for each character of the result the
    /// offset it had in <paramref name="text"/>. The same text when it has none (and a null map).
    /// </summary>
    public static string WithoutControls(string text, out int[]? sourceOffsets)
    {
        sourceOffsets = null;
        if (!text.Any(IsControl))
            return text;
        var kept = new System.Text.StringBuilder(text.Length);
        var offsets = new List<int>(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (IsControl(text[i]))
                continue;
            kept.Append(text[i]);
            offsets.Add(i);
        }
        sourceOffsets = [.. offsets];
        return kept.ToString();
    }

    /// <summary>
    /// Translates <paramref name="text"/> to braille without its direction marks, mapping back to
    /// offsets in <paramref name="text"/>: a mark shares the cell of the character after it, so a
    /// cursor on it is shown, and routed to, there.
    /// </summary>
    public static BrailleTranslation Translate(IBrailleTranslator translator, string text, int? cursorOffset, bool expandWordAtCursor)
    {
        var shown = WithoutControls(text, out var offsets);
        if (offsets is null)
            return translator.Translate(text, cursorOffset, expandWordAtCursor);

        // The cursor's place among the characters shown: the first shown at or after it
        int? shownCursor = cursorOffset is { } cursor ? CountBefore(offsets, cursor) : null;
        var translation = translator.Translate(shown, shownCursor, expandWordAtCursor);

        var cellForChar = new int[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            int shownIndex = CountBefore(offsets, i);
            cellForChar[i] = shownIndex < translation.CellForChar.Length ? translation.CellForChar[shownIndex] : translation.Cells.Length;
        }
        var charForCell = translation.CharForCell
            .Select(shownIndex => shownIndex < offsets.Length ? offsets[shownIndex] : text.Length)
            .ToArray();
        return translation with { CellForChar = cellForChar, CharForCell = charForCell };
    }

    // How many shown characters come from before source offset `offset`
    private static int CountBefore(int[] offsets, int offset)
    {
        int index = Array.BinarySearch(offsets, offset);
        return index >= 0 ? index : ~index;
    }
}
