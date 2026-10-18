using System.Globalization;
using System.Text;

namespace WingetUni.Core.Parsing;

/// <summary>
/// Measures how many terminal columns a string occupies.
/// </summary>
/// <remarks>
/// Package managers pad their tables by display width rather than by character count, which
/// was measured against real output: a package named "中文Git" occupies seven columns but is
/// only five characters, and the row that contains it is two characters shorter than the
/// header while ending in the same column. Slicing such a row by character index would cut in
/// the wrong place, so column boundaries are converted through this measurement.
/// </remarks>
public static class DisplayWidth
{
    /// <summary>Columns occupied by <paramref name="text"/>.</summary>
    public static int Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += MeasureRune(rune);
        }

        return width;
    }

    /// <summary>
    /// Columns occupied by one character: two for East Asian wide and emoji characters, none
    /// for combining marks, one otherwise.
    /// </summary>
    public static int MeasureRune(Rune rune)
    {
        var value = rune.Value;

        if (value == 0)
        {
            return 0;
        }

        return IsZeroWidth(value) ? 0 : IsWide(value) ? 2 : 1;
    }

    private static bool IsZeroWidth(int value)
    {
        // Control characters take no room, and neither do combining marks, which decorate the
        // character in front of them.
        if (value < 0x20 || (value >= 0x7F && value < 0xA0))
        {
            return true;
        }

        if (value is 0x200B or 0x200C or 0x200D or 0xFEFF)
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(value);
        return category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark;
    }

    /// <summary>
    /// The East Asian Wide and Fullwidth ranges, plus emoji. These are the code points that
    /// occupy two terminal columns.
    /// </summary>
    private static bool IsWide(int value) => value switch
    {
        >= 0x1100 and <= 0x115F => true,   // Hangul Jamo
        >= 0x2E80 and <= 0x303E => true,   // CJK radicals, Kangxi radicals, CJK symbols
        >= 0x3041 and <= 0x33FF => true,   // Kana and CJK compatibility
        >= 0x3400 and <= 0x4DBF => true,   // CJK unified ideographs extension A
        >= 0x4E00 and <= 0x9FFF => true,   // CJK unified ideographs
        >= 0xA000 and <= 0xA4CF => true,   // Yi syllables
        >= 0xA960 and <= 0xA97F => true,   // Hangul Jamo extended A
        >= 0xAC00 and <= 0xD7A3 => true,   // Hangul syllables
        >= 0xF900 and <= 0xFAFF => true,   // CJK compatibility ideographs
        >= 0xFE10 and <= 0xFE19 => true,   // Vertical forms
        >= 0xFE30 and <= 0xFE6F => true,   // CJK compatibility forms
        >= 0xFF00 and <= 0xFF60 => true,   // Fullwidth forms
        >= 0xFFE0 and <= 0xFFE6 => true,   // Fullwidth signs
        >= 0x1F300 and <= 0x1F64F => true, // Emoji
        >= 0x1F680 and <= 0x1F6FF => true, // Transport emoji
        >= 0x1F900 and <= 0x1F9FF => true, // Supplemental emoji
        >= 0x1FA70 and <= 0x1FAFF => true,
        >= 0x20000 and <= 0x3FFFD => true, // CJK extensions B and beyond
        _ => false,
    };
}
