namespace WingetUni.Core.Models;

/// <summary>
/// A package as reported by exactly one package manager.
/// </summary>
/// <remarks>
/// The same logical application can be installed through several managers at once (for
/// example Winget and Chocolatey), so a package is identified by <see cref="ManagerId"/>
/// together with <see cref="Id"/>, exposed as <see cref="Key"/>.
///
/// Version semantics differ per listing, and the three version properties keep that
/// distinction instead of collapsing it:
/// <list type="bullet">
///   <item>a search or discovery result sets only <see cref="AvailableVersion"/>;</item>
///   <item>an installed listing sets <see cref="InstalledVersion"/> and, when the manager
///   knows about a newer release, <see cref="UpdateVersion"/>.</item>
/// </list>
/// </remarks>
public sealed record Package
{
    /// <summary>Stable identifier of the manager that produced this package, for example "winget".</summary>
    public required string ManagerId { get; init; }

    /// <summary>Manager-specific package identifier, for example "Git.Git".</summary>
    public required string Id { get; init; }

    /// <summary>Display name, for example "Git".</summary>
    public required string Name { get; init; }

    /// <summary>Version currently present on this machine, or null when the package is not installed.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>Version the manager would install right now, or null when it does not report one.</summary>
    public string? AvailableVersion { get; init; }

    /// <summary>
    /// Version the manager reports as an upgrade for the installed package. Only a manager
    /// can know this, so its presence is the update signal rather than a local version
    /// comparison: Winget reports installed versions it cannot order reliably, such as
    /// "&lt; 20.0.1.256".
    /// </summary>
    public string? UpdateVersion { get; init; }

    /// <summary>Source within the manager, for example "winget" or "msstore".</summary>
    public string? Source { get; init; }

    /// <summary>How the manager matched a search term, for example "Tag: git".</summary>
    public string? Match { get; init; }

    public bool IsInstalled => InstalledVersion is not null;

    public bool HasUpdate => UpdateVersion is not null;

    /// <summary>Stable identity of the package within the application, used as a dictionary and selection key.</summary>
    public string Key => $"{ManagerId}:{Id}";
}
