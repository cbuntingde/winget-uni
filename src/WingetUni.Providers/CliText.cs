using System.Text.RegularExpressions;

namespace WingetUni.Providers;

/// <summary>
/// Text helpers for the providers whose tools print columns rather than JSON.
/// </summary>
/// <remarks>
/// Winget and the .NET CLI print tables whose headings sit above a rule of dashes, which
/// <see cref="WingetUni.Core.Parsing.TextTable"/> already handles. Scoop prints a table of the
/// same shape but with spaces between the dash groups of its rule, so a heading line is found
/// here by looking for the headings themselves rather than by looking for the rule.
/// </remarks>
internal static class CliText
{
    /// <summary>A run of two or more spaces separates columns.</summary>
    private static readonly Regex ColumnSeparator = new(@"\s{2,}", RegexOptions.Compiled);

    /// <summary>Splits a line into cells on runs of two or more spaces.</summary>
    public static IReadOnlyList<string> SplitColumns(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var cells = new List<string>();

        foreach (var cell in ColumnSeparator.Split(line.Trim()))
        {
            if (cell.Length > 0)
            {
                cells.Add(cell);
            }
        }

        return cells;
    }

    /// <summary>Joins captured output lines back together, for tools that print JSON.</summary>
    public static string JoinLines(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Returns the first token in <paramref name="line"/> that looks like a version number,
    /// which is an optional "v", a digit, and a dot somewhere after it. Null when none does.
    /// </summary>
    /// <remarks>
    /// This exists because several tools prefix their version with a product name, as in
    /// "pip 26.2.1 from C:\Python314", where taking the whole line would report the path.
    /// </remarks>
    public static string? VersionToken(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        foreach (var raw in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim(',', ';', ':', ')', '(', '.');
            var digits = token.Length > 0 && token[0] is 'v' or 'V' ? token[1..] : token;

            if (digits.Length > 0 && char.IsAsciiDigit(digits[0]) && digits.Contains('.'))
            {
                return digits;
            }
        }

        return null;
    }

    /// <summary>True when a line holds only dashes and spaces, the rule under a table's headings.</summary>
    public static bool IsRuleLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var hasDash = false;

        foreach (var character in line)
        {
            if (character == '-')
            {
                hasDash = true;
            }
            else if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return hasDash;
    }

    /// <summary>
    /// Reads pipe-delimited rows such as the "name|version" lines Chocolatey prints with
    /// <c>--limit-output</c>, keeping rows that hold at least <paramref name="minimumFields"/>
    /// fields. Lines with no pipe carry headings or summaries and are ignored.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> SplitPipeRows(IEnumerable<string> lines, int minimumFields)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<IReadOnlyList<string>>();

        foreach (var line in lines)
        {
            if (!line.Contains('|'))
            {
                continue;
            }

            var cells = line.Split('|');
            if (cells.Length < minimumFields)
            {
                continue;
            }

            for (var index = 0; index < cells.Length; index++)
            {
                cells[index] = cells[index].Trim();
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>
    /// Parses a table by locating the heading line that holds every heading in
    /// <paramref name="headings"/>, then reading the rows beneath it. Each row is keyed by the
    /// headings that were found.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseTable(
        IEnumerable<string> lines,
        params string[] headings)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(headings);

        var all = lines as IReadOnlyList<string> ?? [.. lines];
        var headerIndex = FindHeaderIndex(all, headings);
        if (headerIndex < 0)
        {
            return [];
        }

        var columns = SplitColumns(all[headerIndex]);
        var rows = new List<IReadOnlyDictionary<string, string>>();

        for (var index = headerIndex + 1; index < all.Count; index++)
        {
            var line = all[index];
            if (line.Trim().Length == 0 || IsRuleLine(line))
            {
                continue;
            }

            var cells = SplitColumns(line);
            if (cells.Count < 2)
            {
                // A trailing sentence such as "Everything is up to date." collapses to one cell.
                continue;
            }

            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var column = 0; column < columns.Count; column++)
            {
                row[columns[column]] = column < cells.Count ? cells[column] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>Returns a row's value for a heading, or null when the heading is absent or blank.</summary>
    public static string? Cell(IReadOnlyDictionary<string, string> row, string heading)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.TryGetValue(heading, out var value) && value.Length > 0 ? value : null;
    }

    private static int FindHeaderIndex(IReadOnlyList<string> lines, IReadOnlyList<string> headings)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var columns = SplitColumns(lines[index]);
            if (columns.Count < 2)
            {
                continue;
            }

            var matches = true;
            foreach (var heading in headings)
            {
                if (!columns.Any(column => string.Equals(column, heading, StringComparison.OrdinalIgnoreCase)))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                return index;
            }
        }

        return -1;
    }
}
