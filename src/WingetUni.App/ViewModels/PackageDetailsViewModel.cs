using WingetUni.Core.Models;

namespace WingetUni.App.ViewModels;

/// <summary>One key and value in the details window's full field list.</summary>
public sealed record DetailFieldViewModel(string Key, string Value);

/// <summary>Full metadata for one package, ready to display.</summary>
public sealed class PackageDetailsViewModel
{
    public PackageDetailsViewModel(PackageDetails details, string managerDisplayName)
    {
        ArgumentNullException.ThrowIfNull(details);

        Details = details;
        ManagerName = managerDisplayName;
    }

    public PackageDetails Details { get; }

    public string ManagerName { get; }

    public string Name => Details.Name ?? Details.Id;

    public string Id => Details.Id;

    public string? Version => Details.Version;

    public string? Publisher => Details.Publisher;

    public string? Author => Details.Author;

    public string? Homepage => Details.Homepage;

    public string? License => Details.License ?? Details.LicenseUrl;

    public string? ReleaseDate => Details.ReleaseDate;

    public string? Copyright => Details.Copyright;

    public string? Description => Details.Description;

    public string? InstallerType => Details.InstallerType;

    public string? InstallerUrl => Details.InstallerUrl;

    public IReadOnlyList<string> Tags => Details.Tags;

    public IReadOnlyList<string> Documentation => Details.Documentation;

    public string TagsText => string.Join(", ", Details.Tags);

    /// <summary>The labelled fields worth showing above the full list, in a fixed order.</summary>
    public IReadOnlyList<DetailFieldViewModel> Summary =>
    [
        .. Labeled("Publisher", Publisher),
        .. Labeled("Author", Author),
        .. Labeled("Version", Version),
        .. Labeled("Release date", ReleaseDate),
        .. Labeled("License", License),
        .. Labeled("Installer type", InstallerType),
        .. Labeled("Homepage", Homepage),
        .. Labeled("Installer URL", InstallerUrl),
        .. Labeled("Copyright", Copyright),
    ];

    /// <summary>
    /// Every field the manager reported, so nothing it returned is hidden just because the
    /// interface does not have a labelled place for it.
    /// </summary>
    public IReadOnlyList<DetailFieldViewModel> Fields =>
    [
        .. Details.Fields
            .OrderBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .Select(field => new DetailFieldViewModel(field.Key, field.Value)),
    ];

    private static IEnumerable<DetailFieldViewModel> Labeled(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            yield return new DetailFieldViewModel(key, value);
        }
    }
}
