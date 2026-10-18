using WingetUni.Core.Abstractions;

namespace WingetUni.Core.Services;

/// <summary>
/// Holds the package managers this application can drive and reports which are present.
/// </summary>
public sealed class PackageManagerRegistry
{
    public PackageManagerRegistry(IEnumerable<IPackageManagerProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        Providers = [.. providers];
    }

    public IReadOnlyList<IPackageManagerProvider> Providers { get; }

    /// <summary>Managers whose tool was found on this machine.</summary>
    public IReadOnlyList<IPackageManagerProvider> Available =>
        [.. Providers.Where(provider => provider.IsAvailable)];

    public IPackageManagerProvider? Find(string managerId) =>
        Providers.FirstOrDefault(provider => string.Equals(provider.Id, managerId, StringComparison.Ordinal));

    /// <summary>
    /// Checks every manager. Managers are probed concurrently so that one slow or missing tool
    /// does not delay the others.
    /// </summary>
    /// <remarks>
    /// A probe does not throw for a missing tool: the process runner reports that as a failed
    /// result, which the provider records in <see cref="IPackageManagerProvider.UnavailableReason"/>.
    /// An exception here therefore means a defect, and it is allowed to surface rather than
    /// being hidden behind an "unavailable" label.
    /// </remarks>
    public Task ProbeAllAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(Providers.Select(provider => provider.ProbeAsync(cancellationToken)));
}
