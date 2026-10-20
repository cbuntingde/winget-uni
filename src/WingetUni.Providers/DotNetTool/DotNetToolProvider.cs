using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Parsing;
using WingetUni.Core.Processes;
using WingetUni.Core.Versions;

namespace WingetUni.Providers.DotNetTool;

/// <summary>
/// Global .NET tools, installed with <c>dotnet tool</c>.
/// </summary>
/// <remarks>
/// The .NET CLI prints fixed-width tables with a rule of dashes, which
/// <see cref="TextTable"/> already parses. It has no command that lists available updates, so
/// each installed tool is looked up with <c>dotnet tool search</c> and the versions are compared
/// with <see cref="VersionComparison"/>. That is one extra command per installed tool, which is
/// acceptable because a machine normally has only a handful.
/// </remarks>
public sealed class DotNetToolProvider : CliPackageManagerProvider
{
    public const string ProviderId = "dotnet-tool";

    public const string SourceName = "nuget.org";

    /// <summary>How many search results to scan when looking for one tool by its exact id.</summary>
    private const int LookupTake = 5;

    public DotNetToolProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => ".NET Tool";

    protected override string ExecutableName => "dotnet";

    public override async Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var result = await RunReadAsync(
            ["tool", "search", query, "--take", Math.Max(limit, 1).ToString()],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return [];
        }

        return ReadSearch(TextTable.Parse(result.StandardOutput));
    }

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["tool", "list", "--global"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return ReadInstalled(TextTable.Parse(result.StandardOutput));
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        var installed = await ListInstalledAsync(cancellationToken).ConfigureAwait(false);
        var packages = new List<Package>();

        foreach (var package in installed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (package.InstalledVersion is null)
            {
                continue;
            }

            // One tool's lookup failing must not discard the updates already found: a
            // transient registry error for one id is reported by skipping that tool.
            string? latest;
            try
            {
                latest = await FindLatestAsync(package.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                continue;
            }

            if (latest is null || VersionComparison.Compare(latest, package.InstalledVersion) <= 0)
            {
                continue;
            }

            packages.Add(package with { UpdateVersion = latest });
        }

        return packages;
    }

    public override async Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await RunReadAsync(
            ["tool", "search", package.Id, "--take", LookupTake.ToString()],
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var table = TextTable.Parse(result.StandardOutput);
        var idColumn = Resolve(table.Headers, "Package ID", 0);
        var versionColumn = Resolve(table.Headers, "Latest Version", 1);
        var authorsColumn = Resolve(table.Headers, "Authors", 2);
        var downloadsColumn = Resolve(table.Headers, "Downloads", 3);

        var row = table.Rows.FirstOrDefault(candidate =>
            string.Equals(ParsedTable.Cell(candidate, idColumn), package.Id, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var authors = ParsedTable.Cell(row, authorsColumn);
        var downloads = ParsedTable.Cell(row, downloadsColumn);

        if (authors is not null)
        {
            fields["Authors"] = authors;
        }

        if (downloads is not null)
        {
            fields["Downloads"] = downloads;
        }

        return new PackageDetails
        {
            ManagerId = ProviderId,
            Id = package.Id,
            Name = package.Name,
            Version = ParsedTable.Cell(row, versionColumn),
            Publisher = authors,
            Author = authors,
            Fields = fields,
        };
    }

    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        var arguments = new List<string> { "tool", CommandFor(operation) };

        // A tool is installed either globally or into a folder, never both, so a chosen
        // location replaces the global switch rather than joining it.
        if (!string.IsNullOrWhiteSpace(options.Location))
        {
            arguments.Add("--tool-path");
            arguments.Add(options.Location!);
        }
        else
        {
            arguments.Add("--global");
        }

        arguments.Add(package.Id);

        if (!string.IsNullOrWhiteSpace(options.Version))
        {
            arguments.Add("--version");
            arguments.Add(options.Version!);
        }

        if (!string.IsNullOrWhiteSpace(options.Source))
        {
            arguments.Add("--add-source");
            arguments.Add(options.Source!);
        }

        if (operation != PackageOperation.Uninstall && !string.IsNullOrWhiteSpace(options.CustomArguments))
        {
            arguments.AddRange(ArgumentSplitter.Split(options.CustomArguments));
        }

        return arguments;
    }

    /// <summary>Looks up the newest published version of one tool, or null when it is not found.</summary>
    /// <remarks>
    /// A failed lookup returns null rather than throwing, so the caller can skip one tool
    /// without abandoning the rest. Cancellation still propagates.
    /// </remarks>
    private async Task<string?> FindLatestAsync(string id, CancellationToken cancellationToken)
    {
        ProcessRunResult result;
        try
        {
            result = await RunReadAsync(
                ["tool", "search", id, "--take", LookupTake.ToString()],
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        if (result.ExitCode != 0)
        {
            return null;
        }

        var table = TextTable.Parse(result.StandardOutput);
        var idColumn = Resolve(table.Headers, "Package ID", 0);
        var versionColumn = Resolve(table.Headers, "Latest Version", 1);

        return table.Rows
            .Where(row => string.Equals(ParsedTable.Cell(row, idColumn), id, StringComparison.OrdinalIgnoreCase))
            .Select(row => ParsedTable.Cell(row, versionColumn))
            .FirstOrDefault(version => version is not null);
    }

    private static IReadOnlyList<Package> ReadSearch(ParsedTable table)
    {
        var id = Resolve(table.Headers, "Package ID", 0);
        var version = Resolve(table.Headers, "Latest Version", 1);
        var packages = new List<Package>();

        foreach (var row in table.Rows)
        {
            var identifier = ParsedTable.Cell(row, id);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                continue;
            }

            packages.Add(new Package
            {
                ManagerId = ProviderId,
                Id = identifier,
                Name = identifier,
                AvailableVersion = ParsedTable.Cell(row, version),
                Source = SourceName,
            });
        }

        return packages;
    }

    private static IReadOnlyList<Package> ReadInstalled(ParsedTable table)
    {
        var id = Resolve(table.Headers, "Package Id", 0);
        var version = Resolve(table.Headers, "Version", 1);
        var packages = new List<Package>();

        foreach (var row in table.Rows)
        {
            var identifier = ParsedTable.Cell(row, id);
            if (string.IsNullOrWhiteSpace(identifier))
            {
                continue;
            }

            packages.Add(new Package
            {
                ManagerId = ProviderId,
                Id = identifier,
                Name = identifier,
                InstalledVersion = ParsedTable.Cell(row, version),
                Source = SourceName,
            });
        }

        return packages;
    }

    /// <summary>Locates a column by heading, falling back to its usual position.</summary>
    private static int Resolve(IReadOnlyList<string> headers, string heading, int fallback)
    {
        for (var index = 0; index < headers.Count; index++)
        {
            if (string.Equals(headers[index], heading, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return fallback;
    }

    private static string CommandFor(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "install",
        PackageOperation.Update => "update",
        PackageOperation.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };
}
