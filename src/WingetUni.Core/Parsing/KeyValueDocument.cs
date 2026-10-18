namespace WingetUni.Core.Parsing;

/// <summary>One "Key: value" entry, together with the more-indented lines that follow it.</summary>
public sealed record KeyValueEntry
{
    public required string Key { get; init; }

    /// <summary>The text after the colon. Empty when the entry introduces a block.</summary>
    public required string Value { get; init; }

    /// <summary>Indentation depth of this entry, counted in characters.</summary>
    public required int Indent { get; init; }

    /// <summary>Every more-indented line below the entry, with that indentation removed.</summary>
    public required IReadOnlyList<string> Lines { get; init; }

    /// <summary>
    /// The lines below the entry interpreted as child entries. Empty when the block is prose,
    /// such as a description or a tag list.
    /// </summary>
    public required IReadOnlyList<KeyValueEntry> Children { get; init; }

    /// <summary>
    /// The whole block as text. Always populated, so a block that is not detected as a section
    /// still yields its content.
    /// </summary>
    public string BlockText => string.Join(Environment.NewLine, Lines);

    public KeyValueEntry? FindChild(string key) =>
        Children.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Returns a child's value, or null when the child is absent or is itself a block.</summary>
    public string? ChildValue(string key)
    {
        var child = FindChild(key);
        return child is null || child.Value.Length == 0 ? null : child.Value;
    }
}

/// <summary>
/// Parses the indented "Key: value" listings that package managers print for one package,
/// where a key with no value on its own line introduces an indented block.
/// </summary>
/// <remarks>
/// Nesting is followed to any depth because the metadata really is nested: a Winget listing
/// reaches four levels through "Installer" and its "Dependencies" list.
///
/// A block is treated as a section of child entries only when its own indent level contains a
/// colon. Prose blocks therefore stay intact even when they contain a colon, as a description
/// with a URL does. Because <see cref="KeyValueEntry.BlockText"/> is always populated, a block
/// that is read as prose instead of as a section still yields its full text, so no reported
/// information is lost either way.
/// </remarks>
public sealed class KeyValueDocument
{
    private KeyValueDocument(IReadOnlyList<KeyValueEntry> entries)
    {
        Entries = entries;
    }

    public IReadOnlyList<KeyValueEntry> Entries { get; }

    public static KeyValueDocument Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var measured = new List<(int Indent, string Text)>();

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var indent = 0;
            while (indent < line.Length && line[indent] is ' ' or '\t')
            {
                indent++;
            }

            measured.Add((indent, line[indent..].TrimEnd()));
        }

        return new KeyValueDocument(ParseEntries(measured, 0));
    }

    /// <summary>Reads the entries that sit at exactly <paramref name="indent"/>, absorbing deeper lines.</summary>
    private static List<KeyValueEntry> ParseEntries(List<(int Indent, string Text)> lines, int indent)
    {
        var entries = new List<KeyValueEntry>();

        for (var index = 0; index < lines.Count; index++)
        {
            var (lineIndent, text) = lines[index];
            if (lineIndent != indent)
            {
                continue;
            }

            var blockEnd = index + 1;
            while (blockEnd < lines.Count && lines[blockEnd].Indent > indent)
            {
                blockEnd++;
            }

            var block = lines.GetRange(index + 1, blockEnd - index - 1);
            var separator = text.IndexOf(':');

            entries.Add(new KeyValueEntry
            {
                Key = (separator < 0 ? text : text[..separator]).Trim(),
                Value = separator < 0 ? string.Empty : text[(separator + 1)..].Trim(),
                Indent = indent,
                Lines = block.ConvertAll(line => line.Text),
                Children = BuildChildren(block, indent),
            });

            index = blockEnd - 1;
        }

        return entries;
    }

    private static IReadOnlyList<KeyValueEntry> BuildChildren(List<(int Indent, string Text)> block, int parentIndent)
    {
        if (block.Count == 0)
        {
            return [];
        }

        var childIndent = block.Min(line => line.Indent);
        if (childIndent <= parentIndent)
        {
            return [];
        }

        var looksLikeSection = block.Any(line => line.Indent == childIndent && line.Text.Contains(':'));
        return looksLikeSection ? ParseEntries(block, childIndent) : [];
    }

    /// <summary>Returns the value of a top-level entry, or null when it is absent or is a block.</summary>
    public string? GetValue(string key)
    {
        var entry = Find(key);
        return entry is null || entry.Value.Length == 0 ? null : entry.Value;
    }

    /// <summary>Returns the full text of a top-level entry, including any indented block.</summary>
    public string? GetText(string key)
    {
        var entry = Find(key);
        if (entry is null)
        {
            return null;
        }

        if (entry.Lines.Count == 0)
        {
            return entry.Value.Length == 0 ? null : entry.Value;
        }

        return entry.Value.Length == 0
            ? entry.BlockText
            : entry.Value + Environment.NewLine + entry.BlockText;
    }

    /// <summary>Returns the indented lines of a top-level entry, empty when it has none.</summary>
    public IReadOnlyList<string> GetBlockLines(string key) => Find(key)?.Lines ?? [];

    /// <summary>Returns the child entries of a top-level entry, empty when it has none.</summary>
    public IReadOnlyList<KeyValueEntry> GetChildren(string key) => Find(key)?.Children ?? [];

    private KeyValueEntry? Find(string key) =>
        Entries.FirstOrDefault(entry => string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase));
}
