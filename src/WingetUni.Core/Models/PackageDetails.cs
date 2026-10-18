namespace WingetUni.Core.Models;

/// <summary>
/// Full metadata for a single package, as reported by its manager.
/// </summary>
/// <remarks>
/// Managers expose different fields, so the named properties cover the ones the interface
/// shows and <see cref="Fields"/> keeps every key the manager reported. Nothing a manager
/// returns is discarded, which keeps the detail view useful for managers whose field set
/// differs from the one anticipated here.
/// </remarks>
public sealed record PackageDetails
{
    public required string ManagerId { get; init; }

    public required string Id { get; init; }

    public string? Name { get; init; }

    public string? Version { get; init; }

    public string? Publisher { get; init; }

    public string? PublisherUrl { get; init; }

    public string? PublisherSupportUrl { get; init; }

    public string? Author { get; init; }

    public string? Moniker { get; init; }

    /// <summary>Long description. Managers wrap this across several lines, preserved here.</summary>
    public string? Description { get; init; }

    public string? Homepage { get; init; }

    public string? License { get; init; }

    public string? LicenseUrl { get; init; }

    public string? Copyright { get; init; }

    public string? ReleaseNotes { get; init; }

    public string? ReleaseNotesUrl { get; init; }

    /// <summary>Date the published installer was released, when the manager reports one.</summary>
    public string? ReleaseDate { get; init; }

    public string? InstallerType { get; init; }

    public string? InstallerUrl { get; init; }

    public string? InstallerSha256 { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Documentation entries as "label: url", in the order the manager printed them.</summary>
    public IReadOnlyList<string> Documentation { get; init; } = [];

    /// <summary>
    /// Every field the manager reported, keyed exactly as the manager named it. Nested fields
    /// are prefixed with their section, for example "Installer.Installer Type".
    /// </summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
