using System.Text;
using System.Text.RegularExpressions;

namespace WingetUni.Core.Parsing;

/// <summary>A parsed CLI table: its column headings and one cell list per data row.</summary>
/// <param name="Headers">Column headings in the order the tool printed them.</param>
/// <param name="Rows">One entry per data row, each holding one cell per column.</param>
public sealed record ParsedTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public static readonly ParsedTable Empty = new([], []);

    public bool HasRows => Rows.Count > 0;

    /// <summary>Reads a cell, returning null when the row is too short to have that column.</summary>
    public static string? Cell(IReadOnlyList<string> row, int columnIndex) =>
        columnIndex >= 0 && columnIndex < row.Count ? row[columnIndex] : null;
}

/// <summary>
/// Parses the fixed-width tables that command line package managers print.
/// </summary>
/// <remarks>
/// Winget and similar tools offer no machine-readable output, so their tables are the only
/// input available. The layout was measured against Winget 1.29 rather than assumed:
/// <list type="bullet">
///   <item>columns are widened to the widest cell and are never truncated with an ellipsis,
///   so a cell value survives intact;</item>
///   <item>the trailing column is not right-padded, so rows have different lengths;</item>
///   <item>padding is measured in display columns, so a row containing a wide character such
///   as 中 is shorter than the header while ending in the same column;</item>
///   <item>the header line is immediately followed by a rule of dashes, which is what makes
///   the header findable even when warnings precede the table.</item>
/// </list>
/// Column offsets are read from the header line instead of being hard-coded, because every
/// command and every result set produces different offsets. Only the <em>order</em> of the
/// columns matters, not the words in them, which keeps the parser working on a system where
/// the manager is localized.
///
/// A line is accepted as a data row only when it lines up with those columns. The two listings
/// that end with a sentence, "11 upgrades available." and
/// "&lt;additional entries truncated due to result limit&gt;", are rejected by that check rather
/// than by matching their wording.
/// </remarks>
public static class TextTable
{
    /// <summary>A run of two or more spaces separates columns; a single space may appear inside a heading.</summary>
    private static readonly Regex ColumnSeparator = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Parses the first table found in <paramref name="lines"/>.
    /// </summary>
    /// <param name="lines">Complete output of the command, in order.</param>
    /// <param name="minimumColumns">
    /// How many populated cells a line needs before it counts as a data row. Two by default.
    /// </param>
    /// <returns>The parsed table, or <see cref="ParsedTable.Empty"/> when no table was printed.</returns>
    public static ParsedTable Parse(IEnumerable<string> lines, int minimumColumns = 2)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var allLines = lines as IReadOnlyList<string> ?? [.. lines];

        var headerIndex = FindHeaderIndex(allLines);
        if (headerIndex < 0)
        {
            return ParsedTable.Empty;
        }

        var columns = SplitColumns(allLines[headerIndex]);
        var rows = new List<IReadOnlyList<string>>();

        // Skip the header itself and the rule of dashes underneath it.
        for (var index = headerIndex + 2; index < allLines.Count; index++)
        {
            var line = allLines[index];

            if (line.Length == 0 || IsRuleLine(line))
            {
                continue;
            }

            var positions = LocateBoundaries(line, columns);
            if (!IsAlignedWith(line, positions))
            {
                continue;
            }

            var cells = Slice(line, positions);
            if (cells.Count(cell => cell.Length > 0) >= minimumColumns)
            {
                rows.Add(cells);
            }
        }

        return new ParsedTable(columns.Select(column => column.Text).ToList(), rows);
    }

    /// <summary>
    /// Finds the header line: the only line that is directly followed by a rule of dashes.
    /// </summary>
    private static int FindHeaderIndex(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count - 1; index++)
        {
            if (IsRuleLine(lines[index]) || lines[index].Length == 0)
            {
                continue;
            }

            if (IsRuleLine(lines[index + 1]) && SplitColumns(lines[index]).Count >= 2)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsRuleLine(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        foreach (var character in line)
        {
            if (character != '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds each heading and the display column at which its column starts.
    /// </summary>
    private static List<(string Text, int StartColumn)> SplitColumns(string line)
    {
        var columns = new List<(string Text, int StartColumn)>();

        void AddSegment(int start, int end)
        {
            var segment = line[start..end];
            var text = segment.Trim();
            if (text.Length == 0)
            {
                return;
            }

            var contentStart = start + (segment.Length - segment.TrimStart().Length);
            columns.Add((text, DisplayWidth.Measure(line[..contentStart])));
        }

        var start = 0;
        foreach (Match separator in ColumnSeparator.Matches(line))
        {
            AddSegment(start, separator.Index);
            start = separator.Index + separator.Length;
        }

        AddSegment(start, line.Length);

        return columns;
    }

    /// <summary>
    /// Converts each column's display column into the character index where that column starts
    /// in <paramref name="line"/>. Boundaries past the end of the line map to the line's end.
    /// </summary>
    private static int[] LocateBoundaries(string line, List<(string Text, int StartColumn)> columns)
    {
        var positions = new int[columns.Count];
        var column = 0;
        var character = 0;
        var pending = 0;

        while (pending < columns.Count)
        {
            while (pending < columns.Count && columns[pending].StartColumn <= column)
            {
                positions[pending] = character;
                pending++;
            }

            if (pending >= columns.Count || character >= line.Length)
            {
                break;
            }

            var rune = Rune.GetRuneAt(line, character);
            column += DisplayWidth.MeasureRune(rune);
            character += rune.Utf16SequenceLength;
        }

        while (pending < columns.Count)
        {
            positions[pending] = line.Length;
            pending++;
        }

        return positions;
    }

    /// <summary>
    /// True when the line is laid out on the column boundaries, meaning every internal boundary
    /// falls on padding. A sentence that happens to be longer than the first column does not,
    /// which is what separates a summary line from a data row.
    /// </summary>
    private static bool IsAlignedWith(string line, int[] positions)
    {
        for (var index = 1; index < positions.Length; index++)
        {
            var position = positions[index];
            if (position <= 0 || position >= line.Length)
            {
                // The row ended before this column, which is normal for a trailing column that
                // the row does not use.
                continue;
            }

            if (!char.IsWhiteSpace(line[position - 1]) && !char.IsWhiteSpace(line[position]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Cuts a data row into cells at the located column boundaries.</summary>
    private static List<string> Slice(string line, int[] positions)
    {
        var cells = new List<string>(positions.Length);

        for (var index = 0; index < positions.Length; index++)
        {
            var start = positions[index];
            if (start >= line.Length)
            {
                cells.Add(string.Empty);
                continue;
            }

            // The final column runs to the end of the line because it is not padded.
            var end = index + 1 < positions.Length ? Math.Min(positions[index + 1], line.Length) : line.Length;
            cells.Add(line[start..end].Trim());
        }

        return cells;
    }
}
