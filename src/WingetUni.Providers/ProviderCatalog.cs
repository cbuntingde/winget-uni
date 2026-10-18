using WingetUni.Core.Abstractions;
using WingetUni.Core.Services;
using WingetUni.Providers.Chocolatey;
using WingetUni.Providers.DotNetTool;
using WingetUni.Providers.Npm;
using WingetUni.Providers.Pip;
using WingetUni.Providers.PowerShellGallery;
using WingetUni.Providers.Scoop;
using WingetUni.Providers.WinGet;

namespace WingetUni.Providers;

/// <summary>
/// The package managers this application knows how to drive, in the order they are presented.
/// </summary>
/// <remarks>
/// One place lists them, so the interface, the settings screen and the query service all agree
/// on the same set. A manager that is not installed is still listed: its probe records why it is
/// unavailable, which is what lets the settings screen explain the absence instead of hiding it.
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>Creates every provider, freshly probed or not depending on the caller.</summary>
    public static IReadOnlyList<IPackageManagerProvider> CreateAll(IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);

        return
        [
            new WinGetProvider(runner),
            new ChocolateyProvider(runner),
            new ScoopProvider(runner),
            new PipProvider(runner),
            new NpmProvider(runner),
            new DotNetToolProvider(runner),
            new PowerShellGalleryProvider(runner),
        ];
    }

    /// <summary>Creates a registry holding every provider.</summary>
    public static PackageManagerRegistry CreateRegistry(IProcessRunner runner) => new(CreateAll(runner));
}
