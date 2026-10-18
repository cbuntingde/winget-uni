using System.Globalization;
using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Parsing;
using WingetUni.Core.Processes;

namespace WingetUni.Providers.WinGet;

/// <summary>
/// Windows Package Manager. Also reports packages installed through the registry and through
/// MSIX, which is why an installed listing contains entries from sources other than "winget".
/// </summary>
/// <remarks>
/// Every command is passed <c>--disable-interactivity</c>, and agreements are pre-accepted,
/// because this provider runs behind a window with no console attached: a prompt would never
/// be seen and the command would simply hang until its timeout.
/// </remarks>
public sealed class WinGetProvider : CliPackageManagerProvider
{
    public const string ProviderId = "winget";

    /// <summary>Winget refuses a count above this.</summary>
    private const int MaxSearchResults = 1000;

    public WinGetProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => "WinGet";

    protected override string ExecutableName => "winget";

    protected override bool IsEmptyResult(IReadOnlyList<string> arguments, int exitCode) =>
        exitCode is WingetExitCodes.NoApplicationsFound or WingetExitCodes.UpdateNotApplicable;

    public override async Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "search", "--disable-interactivity", "--accept-source-agreements" };

        // Without a query Winget lists everything it knows, which is the useful behaviour for
        // browsing a source rather than searching it.
        if (!string.IsNullOrWhiteSpace(query))
        {
            arguments.Add("--query");
            arguments.Add(query);
        }

        arguments.Add("--count");
        arguments.Add(Math.Clamp(limit, 1, MaxSearchResults).ToString(CultureInfo.InvariantCulture));

        var result = await RunReadAsync(arguments, cancellationToken).ConfigureAwait(false);
        return result.ExitCode != 0
            ? []
            : WingetOutputParser.ParseSearch(TextTable.Parse(result.StandardOutput));
    }

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["list", "--disable-interactivity"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode != 0
            ? []
            : WingetOutputParser.ParseInstalled(TextTable.Parse(result.StandardOutput));
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        // `upgrade` with no arguments only reports; it prints the same columns as `list`, where
        // Version is what is installed and Available is what could be installed.
        var result = await RunReadAsync(["upgrade", "--disable-interactivity"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode != 0
            ? []
            : WingetOutputParser.ParseInstalled(TextTable.Parse(result.StandardOutput));
    }

    public override async Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var arguments = new List<string>
        {
            "show", "--id", package.Id, "--exact", "--disable-interactivity", "--accept-source-agreements",
        };

        if (!string.IsNullOrWhiteSpace(package.Source))
        {
            arguments.Add("--source");
            arguments.Add(package.Source);
        }

        var result = await RunReadAsync(arguments, cancellationToken).ConfigureAwait(false);

        // Packages recorded by the registry or by MSIX cannot be described by Winget, and a
        // listing that could not be read is reported as "no details" rather than as an error.
        return result.ExitCode != 0
            ? null
            : WingetOutputParser.ParseDetails(package.Id, package.Name, result.StandardOutput);
    }

    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        var arguments = new List<string>
        {
            CommandFor(operation),
            "--id",
            package.Id,
            "--exact",
            "--disable-interactivity",
        };

        if (options.AcceptAgreements)
        {
            arguments.Add("--accept-source-agreements");

            // Only installing and upgrading involve a package license.
            if (operation != PackageOperation.Uninstall)
            {
                arguments.Add("--accept-package-agreements");
            }
        }

        if (options.Silent)
        {
            arguments.Add("--silent");
        }

        // The package's own source wins when the user did not choose one, so that a package
        // installed from msstore is not looked up in winget.
        var source = options.Source ?? package.Source;
        if (!string.IsNullOrWhiteSpace(source))
        {
            arguments.Add("--source");
            arguments.Add(source!);
        }

        if (!string.IsNullOrWhiteSpace(options.Version))
        {
            arguments.Add("--version");
            arguments.Add(options.Version!);
        }

        if (!string.IsNullOrWhiteSpace(options.Scope))
        {
            arguments.Add("--scope");
            arguments.Add(options.Scope!);
        }

        // `uninstall` accepts neither an install location nor installer arguments.
        if (operation != PackageOperation.Uninstall)
        {
            if (!string.IsNullOrWhiteSpace(options.Location))
            {
                arguments.Add("--location");
                arguments.Add(options.Location!);
            }

            if (!string.IsNullOrWhiteSpace(options.CustomArguments))
            {
                // `--custom` appends to the package's own switches, which is what a user
                // adding an option expects, unlike `--override` which replaces them.
                arguments.Add("--custom");
                arguments.Add(options.CustomArguments!);
            }
        }

        return arguments;
    }

    protected override OperationResult Interpret(
        PackageOperation operation,
        Package package,
        ProcessRunResult result)
    {
        var outcome = base.Interpret(operation, package, result);

        // A successful exit is the only signal Winget gives, so a success is reported as-is.
        if (outcome.Success || !result.Started || result.Canceled || result.TimedOut || result.ExitCode == 0)
        {
            return outcome;
        }

        var explained = WingetExitCodes.Describe(result.ExitCode);
        if (explained is null)
        {
            return outcome;
        }

        var suffix = WingetExitCodes.NeedsElevation(result.ExitCode)
            ? " Restart the application as administrator to allow this."
            : string.Empty;

        return OperationResult.Fail(
            result.ExitCode,
            $"{explained}{suffix} (WinGet code 0x{result.ExitCode:X8})",
            result.Transcript);
    }

    protected override string DescribeQueryFailure(ProcessRunResult result)
    {
        var explained = WingetExitCodes.Describe(result.ExitCode);
        return explained is null
            ? base.DescribeQueryFailure(result)
            : $"{explained} (WinGet code 0x{result.ExitCode:X8})";
    }

    /// <summary>Maps an operation onto the Winget command that performs it.</summary>
    private static string CommandFor(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "install",
        PackageOperation.Update => "upgrade",
        PackageOperation.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };
}
