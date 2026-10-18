namespace WingetUni.Core.Models;

/// <summary>
/// Options a user can set for a single install, update, or uninstall.
/// </summary>
/// <remarks>
/// Every option is optional. A provider applies only the options its command line supports and
/// ignores the rest, so the same options object can be passed to any manager.
/// </remarks>
public sealed record InstallOptions
{
    /// <summary>Operate on a specific version instead of the newest one.</summary>
    public string? Version { get; init; }

    /// <summary>Restrict the operation to one source of the manager, for example "winget".</summary>
    public string? Source { get; init; }

    /// <summary>"user" or "machine". Providers that cannot honour the request ignore it.</summary>
    public string? Scope { get; init; }

    /// <summary>Install directory, for managers that support one.</summary>
    public string? Location { get; init; }

    /// <summary>
    /// Extra arguments for the package's own installer, exactly as the user typed them.
    /// Kept as one string because that is what the managers expect: Winget forwards it through
    /// a single <c>--custom</c> value rather than as separate arguments.
    /// </summary>
    public string? CustomArguments { get; init; }

    /// <summary>Ask the manager for an unattended operation.</summary>
    public bool Silent { get; init; } = true;

    /// <summary>
    /// Pre-accept source and package agreements so an operation cannot stall on a prompt that
    /// a windowed application has no way to display.
    /// </summary>
    public bool AcceptAgreements { get; init; } = true;

    public static InstallOptions Default { get; } = new();
}
