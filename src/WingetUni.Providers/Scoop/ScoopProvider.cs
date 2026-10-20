using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Parsing;
using WingetUni.Core.Processes;

namespace WingetUni.Providers.Scoop;

/// <summary>
/// Scoop, the per-user command line installer that keeps its packages out of Program Files.
/// </summary>
/// <remarks>
/// Scoop prints its listings as fixed-width tables. It does not lay down a plain rule of dashes
/// under the headings the way Winget does, so the tables are read with
/// <see cref="CliText.ParseTable"/>, which finds the heading line by name.
///
/// Scoop is not installed on every machine. When it is missing, its shim cannot be started and
/// the probe records that as the reason it is unavailable, which leaves the rest of the
/// application working.
/// </remarks>
public sealed class ScoopProvider : CliPackageManagerProvider
{
    public const string ProviderId = "scoop";

    public const string SourceName = "scoop";

    public ScoopProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => "Scoop";

    protected override string ExecutableName => "scoop";

    public override async Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var result = await RunReadAsync(["search", query], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        var rows = CliText.ParseTable(result.StandardOutput, "Name", "Version");

        return
        [
            .. rows
                .Select(row => Build(row, installed: null, update: null))
                .Where(package => package is not null)
                .Take(limit)
                .Select(package => package!),
        ];
    }

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["list"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        var rows = CliText.ParseTable(result.StandardOutput, "Name", "Version");

        return
        [
            .. rows
                .Select(row => Build(row, CliText.Cell(row, "Version"), update: null))
                .Where(package => package is not null)
                .Select(package => package!),
        ];
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        // `scoop status` reports what could be upgraded, with the installed and latest versions
        // side by side, which is exactly the pair the interface needs.
        var result = await RunReadAsync(["status"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        var rows = CliText.ParseTable(result.StandardOutput, "Name", "Installed Version", "Latest Version");

        return
        [
            .. rows
                .Select(row => Build(row, CliText.Cell(row, "Installed Version"), CliText.Cell(row, "Latest Version")))
                .Where(package => package is not null && package.UpdateVersion is not null)
                .Select(package => package!),
        ];
    }

    public override async Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await RunReadAsync(["info", package.Id], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var document = KeyValueDocument.Parse(result.StandardOutput);
        if (document.Entries.Count == 0)
        {
            return null;
        }

        return new PackageDetails
        {
            ManagerId = ProviderId,
            Id = package.Id,
            Name = document.GetValue("Name") ?? package.Name,
            Version = document.GetValue("Version"),
            Description = document.GetText("Description"),
            Homepage = document.GetValue("Website"),
            License = document.GetValue("License"),
            ReleaseDate = document.GetValue("Updated at"),
            Author = document.GetValue("Updated by"),
            Fields = Flatten(document),
        };
    }

    /// <summary>Builds the command line for one operation.</summary>
    /// <remarks>
    /// Scoop has no equivalent for an install location, installer arguments, or a silent
    /// flag: it keeps every package under its own directory and installs unattended by
    /// default. Those options are therefore ignored here, as <see cref="InstallOptions"/>
    /// allows, rather than passed as flags Scoop would reject.
    /// </remarks>
    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        var arguments = new List<string> { CommandFor(operation) };

        // Scoop selects a version with an "app@version" specifier rather than with a flag.
        // Only installing takes a version: updating moves to the newest release and
        // removing takes a plain name.
        var specifier = operation == PackageOperation.Install && !string.IsNullOrWhiteSpace(options.Version)
            ? $"{package.Id}@{options.Version}"
            : package.Id;

        arguments.Add(specifier);

        // A global install places the package under ProgramData, which is the closest Scoop has
        // to a machine-wide scope.
        if (string.Equals(options.Scope, "machine", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("--global");
        }

        return arguments;
    }

    /// <summary>
    /// Overrides the default version reading, because <c>scoop --version</c> prints a labelled
    /// block whose first line is the words "Current Scoop version:" rather than a number.
    /// </summary>
    protected override string? ExtractVersion(ProcessRunResult result)
    {
        foreach (var line in result.StandardOutput)
        {
            if (CliText.VersionToken(line) is { } version)
            {
                return version;
            }
        }

        return base.ExtractVersion(result);
    }

    private static Package? Build(
        IReadOnlyDictionary<string, string> row,
        string? installed,
        string? update)
    {
        var name = CliText.Cell(row, "Name");
        if (name is null)
        {
            return null;
        }

        return new Package
        {
            ManagerId = ProviderId,
            Id = name,
            Name = name,
            InstalledVersion = installed,
            AvailableVersion = installed is null ? CliText.Cell(row, "Version") : null,
            UpdateVersion = update,
            Source = CliText.Cell(row, "Source") ?? SourceName,
        };
    }

    private static IReadOnlyDictionary<string, string> Flatten(KeyValueDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.Entries)
        {
            fields[entry.Key] = entry.Value.Length > 0 ? entry.Value : entry.BlockText;
        }

        return fields;
    }

    private static string CommandFor(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "install",
        PackageOperation.Update => "update",
        PackageOperation.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };
}
