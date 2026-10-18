using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Processes;

namespace WingetUni.Providers.Chocolatey;

/// <summary>
/// Chocolatey, the community package manager for Windows.
/// </summary>
/// <remarks>
/// Every command that can prompt is given <c>-y</c>, because this provider runs with no console
/// attached: a confirmation prompt would never be answered and the command would hang until its
/// timeout. <c>--no-progress</c> keeps the spinner out of the operation log.
///
/// Most Chocolatey packages need administrator rights. This provider does not try to obtain
/// them, so a refusal is reported through the operation result and shown to the user.
/// </remarks>
public sealed class ChocolateyProvider : CliPackageManagerProvider
{
    public const string ProviderId = "chocolatey";

    /// <summary>The single source Chocolatey is configured with by default.</summary>
    public const string SourceName = "chocolatey";

    /// <summary>Chocolatey reports 3010 when the package installed but Windows must restart.</summary>
    private const int InstallSucceededRebootRequired = 3010;

    /// <summary>Chocolatey reports 1641 when the installer started a restart itself.</summary>
    private const int InstallSucceededRebootStarted = 1641;

    public ChocolateyProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => "Chocolatey";

    protected override string ExecutableName => "choco";

    public override async Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Chocolatey has no way to list a source without a term, so a blank search is answered
        // with nothing rather than with an error the user did not cause.
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var result = await RunReadAsync(["search", query, "--limit-output"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode != 0
            ? []
            : ChocolateyOutputParser.ParseSearch(result.StandardOutput, limit);
    }

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["list", "--limit-output"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode != 0 ? [] : ChocolateyOutputParser.ParseInstalled(result.StandardOutput);
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["outdated", "--limit-output"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode != 0 ? [] : ChocolateyOutputParser.ParseOutdated(result.StandardOutput);
    }

    public override async Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await RunReadAsync(["info", package.Id], cancellationToken).ConfigureAwait(false);

        // An id that is not in the source makes `choco info` exit non-zero, which is reported as
        // "no details" rather than as an error.
        return result.ExitCode != 0
            ? null
            : ChocolateyOutputParser.ParseDetails(package.Id, result.StandardOutput);
    }

    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        var arguments = new List<string> { CommandFor(operation), package.Id, "--no-progress", "--yes" };

        if (!string.IsNullOrWhiteSpace(options.Source))
        {
            arguments.Add("--source");
            arguments.Add(options.Source!);
        }

        if (!string.IsNullOrWhiteSpace(options.Version))
        {
            arguments.Add("--version");
            arguments.Add(options.Version!);
        }

        // Installing and upgrading can forward switches to the package's own installer.
        // Removing cannot, and Chocolatey would reject the extra option.
        if (operation != PackageOperation.Uninstall && !string.IsNullOrWhiteSpace(options.CustomArguments))
        {
            arguments.Add("--install-arguments");
            arguments.Add(options.CustomArguments!);
        }

        return arguments;
    }

    protected override OperationResult Interpret(
        PackageOperation operation,
        Package package,
        ProcessRunResult result)
    {
        // 3010 and 1641 are the Windows installer codes for "done, but Windows must restart".
        // Chocolatey passes them through, and they are a success rather than a failure.
        if (result.Started && result.ExitCode is InstallSucceededRebootRequired or InstallSucceededRebootStarted)
        {
            return OperationResult.Ok(
                result.ExitCode,
                $"{OutcomeName(operation)} {package.Name}. Windows must restart to finish.",
                result.Transcript);
        }

        return base.Interpret(operation, package, result);
    }

    private static string CommandFor(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "install",
        PackageOperation.Update => "upgrade",
        PackageOperation.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };
}
