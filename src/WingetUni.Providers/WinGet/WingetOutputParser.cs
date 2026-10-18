using WingetUni.Core.Models;
using WingetUni.Core.Parsing;

namespace WingetUni.Providers.WinGet;

/// <summary>
/// Turns Winget's text output into the application's models.
/// </summary>
/// <remarks>
/// Columns are located by heading name when the heading is one Winget uses, and by position
/// otherwise. The fallback keeps the parser working on a system where Winget is localized,
/// because the column order is the same in every language even when the words are not.
/// </remarks>
internal static class WingetOutputParser
{
    /// <summary>Reads the results of <c>winget search</c>.</summary>
    public static IReadOnlyList<Package> ParseSearch(ParsedTable table)
    {
        var name = Resolve(table.Headers, "Name", 0);
        var id = Resolve(table.Headers, "Id", 1);
        var version = Resolve(table.Headers, "Version", 2);

        // A search prints a Match column only when the search had a query, so its presence
        // shifts the Source column by one.
        var hasMatchColumn = table.Headers.Count >= 5;
        var match = Resolve(table.Headers, "Match", hasMatchColumn ? 3 : -1);
        var source = Resolve(table.Headers, "Source", hasMatchColumn ? 4 : 3);

        return Build(table, id, row => new Package
        {
            ManagerId = WinGetProvider.ProviderId,
            Id = RequiredCell(row, id),
            Name = ParsedTable.Cell(row, name) ?? RequiredCell(row, id),
            AvailableVersion = OptionalCell(row, version),
            Match = OptionalCell(row, match),
            Source = OptionalCell(row, source),
        });
    }

    /// <summary>
    /// Reads the rows of <c>winget list</c> or <c>winget upgrade</c>. Both commands print the
    /// same columns: the installed version, then the version that is available as an update.
    /// </summary>
    public static IReadOnlyList<Package> ParseInstalled(ParsedTable table)
    {
        var name = Resolve(table.Headers, "Name", 0);
        var id = Resolve(table.Headers, "Id", 1);
        var version = Resolve(table.Headers, "Version", 2);
        var available = Resolve(table.Headers, "Available", 3);
        var source = Resolve(table.Headers, "Source", 4);

        return Build(table, id, row => new Package
        {
            ManagerId = WinGetProvider.ProviderId,
            Id = RequiredCell(row, id),
            Name = ParsedTable.Cell(row, name) ?? RequiredCell(row, id),
            InstalledVersion = OptionalCell(row, version),
            UpdateVersion = OptionalCell(row, available),
            Source = OptionalCell(row, source),
        });
    }

    /// <summary>Reads the metadata printed by <c>winget show</c>.</summary>
    /// <param name="id">Package id, which the listing itself does not repeat.</param>
    /// <param name="fallbackName">Name to use when the listing has none, which is the usual case.</param>
    public static PackageDetails? ParseDetails(string id, string? fallbackName, IReadOnlyList<string> lines)
    {
        var document = KeyValueDocument.Parse(StripFoundBanner(lines));
        if (document.Entries.Count == 0)
        {
            return null;
        }

        var installer = document.GetChildren("Installer");
        var installerEntry = document.Entries.FirstOrDefault(
            entry => string.Equals(entry.Key, "Installer", StringComparison.OrdinalIgnoreCase));

        return new PackageDetails
        {
            ManagerId = WinGetProvider.ProviderId,
            Id = id,
            Name = document.GetValue("Name") ?? fallbackName,
            Version = document.GetValue("Version"),
            Publisher = document.GetValue("Publisher"),
            PublisherUrl = document.GetValue("Publisher Url"),
            PublisherSupportUrl = document.GetValue("Publisher Support Url"),
            Author = document.GetValue("Author"),
            Moniker = document.GetValue("Moniker"),
            Description = document.GetText("Description"),
            Homepage = document.GetValue("Homepage"),
            License = document.GetValue("License"),
            LicenseUrl = document.GetValue("License Url"),
            Copyright = document.GetValue("Copyright"),
            ReleaseNotes = document.GetText("Release Notes"),
            ReleaseNotesUrl = document.GetValue("Release Notes Url"),
            ReleaseDate = installerEntry?.ChildValue("Release Date"),
            InstallerType = installerEntry?.ChildValue("Installer Type"),
            InstallerUrl = installerEntry?.ChildValue("Installer Url"),
            InstallerSha256 = installerEntry?.ChildValue("Installer SHA256"),
            Tags = ReadTags(document),
            Documentation = ReadDocumentation(document),
            Fields = Flatten(document, installer),
        };
    }

    /// <summary>
    /// Drops the "Found &lt;name&gt; [&lt;id&gt;]" banner that heads a listing. It is not a
    /// field, and the name and id it carries are already known to the caller.
    /// </summary>
    private static IReadOnlyList<string> StripFoundBanner(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var trimmed = line.TrimEnd();
            var isBanner = trimmed.EndsWith(']') && trimmed.Contains('[');

            return isBanner ? lines.Skip(index + 1).ToList() : lines;
        }

        return lines;
    }

    private static IReadOnlyList<string> ReadTags(KeyValueDocument document)
    {
        var entry = document.Entries.FirstOrDefault(
            item => string.Equals(item.Key, "Tags", StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return [];
        }

        // Tags are usually one per line, but a single tag is printed on the key's own line.
        if (entry.Lines.Count > 0)
        {
            return entry.Lines;
        }

        return entry.Value.Length == 0 ? [] : [entry.Value];
    }

    private static IReadOnlyList<string> ReadDocumentation(KeyValueDocument document) =>
        document.GetChildren("Documentation")
            .Select(child => child.Value.Length == 0 ? child.Key : $"{child.Key}: {child.Value}")
            .ToList();

    private static IReadOnlyDictionary<string, string> Flatten(
        KeyValueDocument document,
        IReadOnlyList<KeyValueEntry> installer)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.Entries)
        {
            if (string.Equals(entry.Key, "Installer", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fields[entry.Key] = entry.Value.Length > 0 ? entry.Value : entry.BlockText;
        }

        foreach (var entry in installer)
        {
            fields[$"Installer.{entry.Key}"] = entry.Value.Length > 0 ? entry.Value : entry.BlockText;
        }

        return fields;
    }

    private static List<Package> Build(
        ParsedTable table,
        int idColumn,
        Func<IReadOnlyList<string>, Package> factory)
    {
        var packages = new List<Package>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            // A row without an id cannot be installed, updated, or removed, so it is skipped
            // rather than shown as something the user could select.
            if (string.IsNullOrWhiteSpace(ParsedTable.Cell(row, idColumn)))
            {
                continue;
            }

            packages.Add(factory(row));
        }

        return packages;
    }

    private static string RequiredCell(IReadOnlyList<string> row, int column) =>
        ParsedTable.Cell(row, column) ?? string.Empty;

    private static string? OptionalCell(IReadOnlyList<string> row, int column)
    {
        var value = column < 0 ? null : ParsedTable.Cell(row, column);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Locates a column by heading, falling back to its usual position.</summary>
    private static int Resolve(IReadOnlyList<string> headers, string heading, int fallback)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], heading, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return fallback;
    }
}
