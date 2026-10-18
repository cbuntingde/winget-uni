using WingetUni.Core.Models;

namespace WingetUni.Core.Abstractions;

/// <summary>
/// One command line package manager, adapted to the operations this application performs.
/// </summary>
/// <remarks>
/// Implementations are expected to be safe to call from any thread and to translate their
/// tool's failures into an <see cref="OperationResult"/> rather than throwing: a package that
/// refuses to install is a normal outcome the interface has to show, not an exception.
/// </remarks>
public interface IPackageManagerProvider
{
    /// <summary>Stable identifier used in settings, saved package lists, and package keys.</summary>
    string Id { get; }

    /// <summary>Name shown in the interface.</summary>
    string DisplayName { get; }

    /// <summary>True once <see cref="ProbeAsync"/> has found the manager on this machine.</summary>
    bool IsAvailable { get; }

    /// <summary>Version of the manager's tool, or null when it is unavailable or does not report one.</summary>
    string? Version { get; }

    /// <summary>Why the manager is unavailable, shown in settings. Null while it is available.</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// Checks whether the manager is installed and records the outcome in
    /// <see cref="IsAvailable"/>, <see cref="Version"/> and <see cref="UnavailableReason"/>.
    /// </summary>
    Task ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Searches the manager's configured sources.</summary>
    /// <param name="query">Search terms. An empty query returns the manager's default listing.</param>
    /// <param name="limit">Maximum number of results to request.</param>
    Task<IReadOnlyList<Package>> SearchAsync(string query, int limit, CancellationToken cancellationToken = default);

    /// <summary>Lists packages already present on this machine.</summary>
    Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists installed packages for which the manager offers a newer version.</summary>
    Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads full metadata for one package, or null when the manager cannot describe it.</summary>
    Task<PackageDetails?> GetDetailsAsync(Package package, CancellationToken cancellationToken = default);

    /// <summary>Installs, updates, or removes one package.</summary>
    /// <param name="progress">Receives the manager's output as it arrives. May be null.</param>
    Task<OperationResult> ExecuteAsync(
        PackageOperation operation,
        Package package,
        InstallOptions options,
        IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
