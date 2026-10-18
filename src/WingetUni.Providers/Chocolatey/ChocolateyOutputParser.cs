using WingetUni.Core.Models;
using WingetUni.Core.Parsing;

namespace WingetUni.Providers.Chocolatey;

/// <summary>
/// Turns Chocolatey's output into the application's models.
/// </summary>
/// <remarks>
/// Chocolatey has a machine-readable mode, <c>--limit-output</c>, that emits one pipe-delimited
/// row per package. It is used for searching, listing and reporting outdated packages. The
/// verbose <c>choco info</c> listing is only needed for the detail view, and it is parsed from
/// "<c>Key: value</c>" lines once the banner above them is removed.
/// </remarks>
internal static class ChocolateyOutputParser
{
    /// <summary>Reads the "name|version" rows of <c>choco search --limit-output</c>.</summary>
    public static IReadOnlyList<Package> ParseSearch(IEnumerable<string> lines, int limit)
    {
        var packages = new List<Package>();

        foreach (var row in CliText.SplitPipeRows(lines, 2))
        {
            var id = row[0];
            if (id.Length == 0)
            {
                continue;
            }

            packages.Add(new Package
            {
                ManagerId = ChocolateyProvider.ProviderId,
                Id = id,
                Name = id,
                AvailableVersion = Value(row, 1),
                Source = ChocolateyProvider.SourceName,
            });

            if (packages.Count >= limit)
            {
                break;
            }
        }

        return packages;
    }

    /// <summary>Reads the "name|version" rows of <c>choco list --limit-output</c>.</summary>
    public static IReadOnlyList<Package> ParseInstalled(IEnumerable<string> lines)
    {
        var packages = new List<Package>();

        foreach (var row in CliText.SplitPipeRows(lines, 2))
        {
            var id = row[0];
            if (id.Length == 0)
            {
                continue;
            }

            packages.Add(new Package
            {
                ManagerId = ChocolateyProvider.ProviderId,
                Id = id,
                Name = id,
                InstalledVersion = Value(row, 1),
                Source = ChocolateyProvider.SourceName,
            });
        }

        return packages;
    }

    /// <summary>
    /// Reads the "name|current|available|pinned" rows of <c>choco outdated --limit-output</c>.
    /// A pinned package is deliberately held back from upgrades, so it is not offered as one.
    /// </summary>
    public static IReadOnlyList<Package> ParseOutdated(IEnumerable<string> lines)
    {
        var packages = new List<Package>();

        foreach (var row in CliText.SplitPipeRows(lines, 4))
        {
            var id = row[0];
            var available = Value(row, 2);

            if (id.Length == 0 || available is null)
            {
                continue;
            }

            if (string.Equals(row[3], "true", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            packages.Add(new Package
            {
                ManagerId = ChocolateyProvider.ProviderId,
                Id = id,
                Name = id,
                InstalledVersion = Value(row, 1),
                UpdateVersion = available,
                Source = ChocolateyProvider.SourceName,
            });
        }

        return packages;
    }

    /// <summary>Reads the metadata printed by <c>choco info</c>, or null when it printed none.</summary>
    public static PackageDetails? ParseDetails(string id, IReadOnlyList<string> lines)
    {
        var (fields, version) = SplitBanner(lines);
        if (fields.Count == 0)
        {
            return null;
        }

        var document = KeyValueDocument.Parse(fields);
        if (document.Entries.Count == 0)
        {
            return null;
        }

        var (title, published) = SplitTitle(document.GetValue("Title"));

        return new PackageDetails
        {
            ManagerId = ChocolateyProvider.ProviderId,
            Id = id,
            Name = title ?? id,
            Version = version,
            Author = document.GetValue("Author") ?? document.GetValue("Owners"),
            Description = document.GetText("Description") ?? document.GetValue("Summary"),
            Homepage = document.GetValue("Software Site"),
            LicenseUrl = document.GetValue("Software License"),
            ReleaseDate = published,
            Tags = SplitTags(document.GetValue("Tags")),
            Documentation = ReadDocumentation(document),
            Fields = Flatten(document),
        };
    }

    /// <summary>
    /// Splits the tool banner off the field block. <c>choco info</c> prints
    /// "Chocolatey v2.7.4" and "&lt;id&gt; &lt;version&gt; [Approved]" before the fields, and the
    /// package's own version appears only on that second line.
    /// </summary>
    /// <remarks>
    /// The fields are indented by one space, and the description below them by two. The common
    /// leading space is removed before the block is handed on, because
    /// <see cref="KeyValueDocument"/> reads top-level entries at column zero.
    /// </remarks>
    private static (IReadOnlyList<string> Fields, string? Version) SplitBanner(IReadOnlyList<string> lines)
    {
        string? version = null;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length == 0)
            {
                continue;
            }

            // The fields are indented and carry a colon; the banner lines are not indented.
            if (char.IsWhiteSpace(line[0]) && line.Contains(':'))
            {
                return (DeIndent(lines, index, Indent(line)), version);
            }

            var token = HeaderVersion(line);
            if (token is not null)
            {
                version = token;
            }
        }

        return ([], version);
    }

    /// <summary>
    /// Removes <paramref name="columns"/> leading spaces from a run of lines, where present, and
    /// stops at the first line that is indented less than those fields.
    /// </summary>
    /// <remarks>
    /// The trailing "1 packages found." summary is the only line <c>choco info</c> prints
    /// un-indented, so the block ends there without having to recognise those words.
    /// </remarks>
    private static IReadOnlyList<string> DeIndent(IReadOnlyList<string> lines, int start, int columns)
    {
        var block = new List<string>(lines.Count - start);

        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index];
            var indent = Indent(line);

            if (columns > 0 && indent < columns)
            {
                break;
            }

            block.Add(line[Math.Min(columns, indent)..]);
        }

        return block;
    }

    private static int Indent(string line)
    {
        var indent = 0;
        while (indent < line.Length && line[indent] is ' ' or '\t')
        {
            indent++;
        }

        return indent;
    }

    /// <summary>Reads the version out of the "&lt;id&gt; &lt;version&gt; [Approved]" header line.</summary>
    private static string? HeaderVersion(string line)
    {
        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 2 ? CliText.VersionToken(tokens[1]) : null;
    }

    /// <summary>Splits the "&lt;title&gt; | Published: &lt;date&gt;" line Chocolatey prints.</summary>
    private static (string? Name, string? Published) SplitTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, null);
        }

        var parts = title.Split('|', 2, StringSplitOptions.TrimEntries);
        string? published = null;

        if (parts.Length > 1 && parts[1].StartsWith("Published:", StringComparison.OrdinalIgnoreCase))
        {
            published = parts[1]["Published:".Length..].Trim();
        }

        return (parts[0].Length > 0 ? parts[0] : null, published);
    }

    private static IReadOnlyList<string> SplitTags(string? tags) =>
        string.IsNullOrWhiteSpace(tags)
            ? []
            : [.. tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    private static IReadOnlyList<string> ReadDocumentation(KeyValueDocument document)
    {
        var links = new List<string>();

        foreach (var key in new[] { "Documentation", "Issues", "Mailing List" })
        {
            if (document.GetValue(key) is { } value)
            {
                links.Add($"{key}: {value}");
            }
        }

        return links;
    }

    private static IReadOnlyDictionary<string, string> Flatten(KeyValueDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.Entries)
        {
            fields[entry.Key] = entry.Value.Length > 0 ? entry.Value : entry.BlockText;
        }

        return fields;
    }

    private static string? Value(IReadOnlyList<string> row, int index) =>
        index < row.Count && row[index].Length > 0 ? row[index] : null;
}
