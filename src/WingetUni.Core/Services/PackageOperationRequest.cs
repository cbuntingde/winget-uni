using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;

namespace WingetUni.Core.Services;

/// <summary>One unit of work queued for a package manager.</summary>
public sealed record PackageOperationRequest
{
    public required IPackageManagerProvider Provider { get; init; }

    public required PackageOperation Operation { get; init; }

    public required Package Package { get; init; }

    public InstallOptions Options { get; init; } = InstallOptions.Default;

    /// <summary>Short description used in the operation list, such as "Installing Git".</summary>
    public string Title => $"{Verb} {Package.Name}";

    private string Verb => Operation switch
    {
        PackageOperation.Install => "Installing",
        PackageOperation.Update => "Updating",
        PackageOperation.Uninstall => "Removing",
        _ => "Processing",
    };
}

public enum OperationState
{
    Pending,
    Running,
    Succeeded,
    Failed,
    Canceled,
}

/// <summary>
/// An immutable snapshot of one queued operation. The queue publishes a new snapshot whenever
/// something changes, which keeps the state safe to read from the interface thread while the
/// operation itself runs on a worker thread.
/// </summary>
public sealed record OperationRecord
{
    public required Guid Id { get; init; }

    public required string ManagerId { get; init; }

    public required string ManagerDisplayName { get; init; }

    public required PackageOperation Operation { get; init; }

    public required Package Package { get; init; }

    public required OperationState State { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Readable outcome once the operation has finished.</summary>
    public string? Message { get; init; }

    /// <summary>Everything the manager printed, in order.</summary>
    public IReadOnlyList<string> Log { get; init; } = [];

    public string Title => $"{Verb} {Package.Name}";

    public bool IsFinished => State is OperationState.Succeeded or OperationState.Failed or OperationState.Canceled;

    private string Verb => Operation switch
    {
        PackageOperation.Install => "Installing",
        PackageOperation.Update => "Updating",
        PackageOperation.Uninstall => "Removing",
        _ => "Processing",
    };
}
