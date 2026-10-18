using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Parsing;
using WingetUni.Core.Processes;

namespace WingetUni.Providers.Pip;

/// <summary>
/// pip, the Python package installer.
/// </summary>
/// <remarks>
/// pip prints clean JSON for its listings, which is used directly. It has no search command:
/// the one it used to have was removed by PyPI. Searching therefore reports why it cannot
/// answer rather than pretending that nothing matched.
///
/// <c>--no-input</c> is passed so that pip can never wait on a prompt, and uninstalling adds
/// <c>-y</c> because pip otherwise asks for confirmation.
/// </remarks>
public sealed class PipProvider : CliPackageManagerProvider
{
    public const string ProviderId = "pip";

    public const string SourceName = "pypi";

    public PipProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => "pip";

    protected override string ExecutableName => "pip";

    public override Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default) =>
        throw new PackageManagerException(
            "pip has no search command, so it cannot answer a search. Enter the exact package name to install it, " +
            "or turn pip off in settings to stop seeing this.");

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["list", "--format=json"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), ReadInstalled);
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(["list", "--outdated", "--format=json"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), ReadOutdated);
    }

    public override async Task<PackageDetails?> GetDetailsAsync(
        Package package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var result = await RunReadAsync(["show", package.Id], cancellationToken).ConfigureAwait(false);
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
            Description = document.GetValue("Summary"),
            Author = document.GetValue("Author"),
            Publisher = document.GetValue("Author-email"),
            Homepage = document.GetValue("Home-page"),
            License = document.GetValue("License-Expression") ?? document.GetValue("License"),
            Fields = Flatten(document),
        };
    }

    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        var arguments = new List<string>();

        switch (operation)
        {
            case PackageOperation.Install:
            case PackageOperation.Update:
                arguments.Add("install");
                arguments.Add(Specifier(package, options));

                if (operation == PackageOperation.Update)
                {
                    arguments.Add("--upgrade");
                }

                arguments.Add("--no-input");
                arguments.Add("--disable-pip-version-check");

                if (string.Equals(options.Scope, "user", StringComparison.OrdinalIgnoreCase))
                {
                    arguments.Add("--user");
                }

                if (operation != PackageOperation.Uninstall && !string.IsNullOrWhiteSpace(options.CustomArguments))
                {
                    arguments.AddRange(ArgumentSplitter.Split(options.CustomArguments));
                }

                break;

            case PackageOperation.Uninstall:
                arguments.Add("uninstall");
                arguments.Add(package.Id);
                arguments.Add("--yes");
                arguments.Add("--disable-pip-version-check");
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation.");
        }

        return arguments;
    }

    /// <summary>
    /// Overrides the default version reading, because <c>pip --version</c> prints
    /// "pip 26.2.1 from C:\Python314\Lib\site-packages\pip", where only the second word is
    /// the version and the last is a path.
    /// </summary>
    protected override string? ExtractVersion(ProcessRunResult result) =>
        CliText.VersionToken(result.StandardOutput.FirstOrDefault()) ?? base.ExtractVersion(result);

    private static Package ReadInstalled(System.Text.Json.JsonElement element) => new()
    {
        ManagerId = ProviderId,
        Id = CliJson.String(element, "name") ?? string.Empty,
        Name = CliJson.String(element, "name") ?? string.Empty,
        InstalledVersion = CliJson.String(element, "version"),
        Source = SourceName,
    };

    private static Package ReadOutdated(System.Text.Json.JsonElement element) => new()
    {
        ManagerId = ProviderId,
        Id = CliJson.String(element, "name") ?? string.Empty,
        Name = CliJson.String(element, "name") ?? string.Empty,
        InstalledVersion = CliJson.String(element, "version"),
        UpdateVersion = CliJson.String(element, "latest_version"),
        Source = SourceName,
    };

    private static string Specifier(Package package, InstallOptions options) =>
        string.IsNullOrWhiteSpace(options.Version) ? package.Id : $"{package.Id}=={options.Version}";

    private static IReadOnlyDictionary<string, string> Flatten(KeyValueDocument document)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in document.Entries)
        {
            fields[entry.Key] = entry.Value.Length > 0 ? entry.Value : entry.BlockText;
        }

        return fields;
    }
}
