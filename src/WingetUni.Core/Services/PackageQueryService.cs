using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;

namespace WingetUni.Core.Services;

/// <summary>The packages a query found, together with anything that went wrong while asking.</summary>
/// <param name="Packages">Results from every manager that answered.</param>
/// <param name="Errors">One entry per manager that failed, worded for display.</param>
public sealed record PackageQueryOutcome(IReadOnlyList<Package> Packages, IReadOnlyList<string> Errors)
{
    public static PackageQueryOutcome Empty { get; } = new([], []);
}

/// <summary>
/// Queries several package managers at once and merges the results.
/// </summary>
/// <remarks>
/// One manager failing does not hide the others: its failure is reported alongside the results
/// from the managers that did answer, because a machine without Chocolatey should still show
/// its Winget packages.
/// </remarks>
public sealed class PackageQueryService
{
    private readonly PackageManagerRegistry _registry;

    public PackageQueryService(PackageManagerRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <summary>
    /// Searches the given managers, or every available manager when
    /// <paramref name="managerIds"/> is null.
    /// </summary>
    public Task<PackageQueryOutcome> SearchAsync(
        string query,
        int limitPerManager,
        IReadOnlyCollection<string>? managerIds = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(managerIds, (provider, token) => provider.SearchAsync(query, limitPerManager, token), cancellationToken);

    public Task<PackageQueryOutcome> ListInstalledAsync(
        IReadOnlyCollection<string>? managerIds = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(managerIds, (provider, token) => provider.ListInstalledAsync(token), cancellationToken);

    public Task<PackageQueryOutcome> ListUpgradableAsync(
        IReadOnlyCollection<string>? managerIds = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(managerIds, (provider, token) => provider.ListUpgradableAsync(token), cancellationToken);

    private async Task<PackageQueryOutcome> RunAsync(
        IReadOnlyCollection<string>? managerIds,
        Func<IPackageManagerProvider, CancellationToken, Task<IReadOnlyList<Package>>> query,
        CancellationToken cancellationToken)
    {
        var providers = Select(managerIds);
        if (providers.Count == 0)
        {
            return PackageQueryOutcome.Empty;
        }

        var results = await Task
            .WhenAll(providers.Select(provider => GuardAsync(provider, query, cancellationToken)))
            .ConfigureAwait(false);

        var packages = new List<Package>();
        var errors = new List<string>();

        foreach (var (found, error) in results)
        {
            packages.AddRange(found);
            if (error is not null)
            {
                errors.Add(error);
            }
        }

        return new PackageQueryOutcome(packages, errors);
    }

    private IReadOnlyList<IPackageManagerProvider> Select(IReadOnlyCollection<string>? managerIds)
    {
        var available = _registry.Available;
        return managerIds is null
            ? available
            : [.. available.Where(provider => managerIds.Contains(provider.Id))];
    }

    private static async Task<(IReadOnlyList<Package> Packages, string? Error)> GuardAsync(
        IPackageManagerProvider provider,
        Func<IPackageManagerProvider, CancellationToken, Task<IReadOnlyList<Package>>> query,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await query(provider, cancellationToken).ConfigureAwait(false), null);
        }
        catch (PackageManagerException exception)
        {
            return ([], $"{provider.DisplayName}: {exception.Message}");
        }
    }
}
