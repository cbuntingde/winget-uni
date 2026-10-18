using System.Text.Json;
using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Parsing;

namespace WingetUni.Providers.Npm;

/// <summary>
/// npm, the package manager for Node.js, restricted to globally installed packages.
/// </summary>
/// <remarks>
/// npm prints JSON for every listing this provider needs, so no text tables are parsed. It is
/// invoked as <c>npm.cmd</c> rather than as <c>npm</c>: on Windows a bare "npm" resolves to the
/// PowerShell script <c>npm.ps1</c>, which cannot be started as a process.
///
/// npm uses a non-zero exit code for "the command worked but found something to report", which
/// is what <c>outdated</c> does when updates exist. That code is treated as a completed query
/// here, otherwise every check for updates would look like a failure.
/// </remarks>
public sealed class NpmProvider : CliPackageManagerProvider
{
    public const string ProviderId = "npm";

    public const string SourceName = "npm";

    /// <summary>Fields requested from <c>npm view</c>, kept short on purpose.</summary>
    private static readonly string[] DetailFields =
    [
        "name", "description", "version", "license", "homepage", "repository", "author", "keywords",
    ];

    public NpmProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => "npm";

    /// <summary>See the type remarks: a bare "npm" would resolve to npm.ps1 on Windows.</summary>
    protected override string ExecutableName => "npm.cmd";

    /// <summary>
    /// npm reports available updates through exit code 1, so <c>outdated</c> is allowed to exit
    /// that way. Every other npm command uses a non-zero code for a real failure, which must
    /// reach the user rather than being read as "no results".
    /// </summary>
    protected override bool IsEmptyResult(IReadOnlyList<string> arguments, int exitCode) =>
        exitCode == 1 && arguments.Contains("outdated", StringComparer.Ordinal);

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
            ["search", "--json", $"--searchlimit={Math.Max(limit, 1)}", query],
            cancellationToken).ConfigureAwait(false);

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), element => new Package
        {
            ManagerId = ProviderId,
            Id = CliJson.String(element, "name") ?? string.Empty,
            Name = CliJson.String(element, "name") ?? string.Empty,
            AvailableVersion = CliJson.String(element, "version"),
            Source = SourceName,
        });
    }

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["ls", "--global", "--depth=0", "--json"], cancellationToken).ConfigureAwait(false);

        using var document = CliJson.Parse(CliText.JoinLines(result.StandardOutput));
        if (document is null
            || document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("dependencies", out var dependencies)
            || dependencies.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var packages = new List<Package>();

        foreach (var dependency in dependencies.EnumerateObject())
        {
            packages.Add(new Package
            {
                ManagerId = ProviderId,
                Id = dependency.Name,
                Name = dependency.Name,
                InstalledVersion = CliJson.String(dependency.Value, "version"),
                Source = SourceName,
            });
        }

        return packages;
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        // `outdated` keys its JSON by package name and reports three versions per entry:
        // what is installed, what the declared range allows, and what the registry has.
        var result = await RunReadAsync(["outdated", "--global", "--json"], cancellationToken).ConfigureAwait(false);

        var packages = new List<Package>();

        using var document = CliJson.Parse(CliText.JoinLines(result.StandardOutput));
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            packages.Add(new Package
            {
                ManagerId = ProviderId,
                Id = entry.Name,
                Name = entry.Name,
                InstalledVersion = CliJson.String(entry.Value, "current"),
                UpdateVersion = CliJson.String(entry.Value, "latest"),
                Source = SourceName,
            });
        }

        return packages;
    }

    public override async Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var arguments = new List<string> { "view", package.Id };
        arguments.AddRange(DetailFields);
        arguments.Add("--json");

        var result = await RunReadAsync(arguments, cancellationToken).ConfigureAwait(false);

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), element => ReadDetails(package, element))
            .FirstOrDefault();
    }

    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        // The package id is qualified with the version, because that is how npm is told which
        // one to take; an update always asks for the newest release.
        var specifier = operation switch
        {
            PackageOperation.Install when !string.IsNullOrWhiteSpace(options.Version) => $"{package.Id}@{options.Version}",
            PackageOperation.Update => $"{package.Id}@{options.Version ?? "latest"}",
            _ => package.Id,
        };

        var arguments = new List<string>
        {
            CommandFor(operation),
            "--global",
            specifier,
        };

        if (!string.IsNullOrWhiteSpace(options.CustomArguments))
        {
            arguments.AddRange(ArgumentSplitter.Split(options.CustomArguments));
        }

        return arguments;
    }

    private static PackageDetails ReadDetails(Package package, JsonElement element)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var description = CliJson.String(element, "description");
        var homepage = CliJson.Text(element, "homepage");
        var repository = CliJson.Text(element, "repository");
        var author = CliJson.Person(element, "author");
        var license = CliJson.String(element, "license");
        var version = CliJson.String(element, "version");

        if (description is not null)
        {
            fields["Description"] = description;
        }

        if (repository is not null)
        {
            fields["Repository"] = repository;
        }

        if (license is not null)
        {
            fields["License"] = license;
        }

        if (author is not null)
        {
            fields["Author"] = author;
        }

        return new PackageDetails
        {
            ManagerId = ProviderId,
            Id = package.Id,
            Name = CliJson.String(element, "name") ?? package.Id,
            Version = version,
            Description = description,
            Author = author,
            Publisher = author,
            Homepage = homepage,
            License = license,
            Tags = CliJson.Strings(element, "keywords"),
            Fields = fields,
        };
    }

    private static string CommandFor(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "install",
        PackageOperation.Update => "install",
        PackageOperation.Uninstall => "uninstall",
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
    };
}
