using WingetUni.Core.Abstractions;
using WingetUni.Core.Models;
using WingetUni.Core.Processes;
using WingetUni.Core.Versions;

namespace WingetUni.Providers.PowerShellGallery;

/// <summary>
/// The PowerShell Gallery, reached through the PowerShellGet module that ships with Windows
/// PowerShell.
/// </summary>
/// <remarks>
/// This is a provider that drives a shell rather than a program: every command is a script
/// handed to <c>powershell.exe</c> with <c>-Command</c>. Three details make that safe:
/// <list type="bullet">
///   <item>the switches <c>-NoProfile -NonInteractive</c> keep the user's profile out of the
///   way and stop any confirmation prompt from waiting for an answer that cannot come;</item>
///   <item>every script sets <c>$ErrorActionPreference='Stop'</c>, so a cmdlet failure ends the
///   script with a non-zero exit code instead of being written to the error stream and
///   forgotten;</item>
///   <item>values that came from a package id or from the search box are enclosed in single
///   quotes with any quote inside them doubled, which is what stops a value from being read as
///   script.</item>
/// </list>
///
/// PowerShellGet cannot talk to the gallery until the NuGet package provider is present, and on
/// a machine where it is absent PowerShellGet asks for permission to install it. That question
/// cannot be shown to anyone here, so the probe installs the provider itself, for the current
/// user only, which needs no administrator rights. Until that is done, every gallery command
/// fails with an unrelated-looking error. See <see cref="EnsureGalleryBody"/>.
/// </remarks>
public sealed class PowerShellGalleryProvider : CliPackageManagerProvider
{
    public const string ProviderId = "powershell-gallery";

    public const string SourceName = "PSGallery";

    /// <summary>
    /// The script that makes the gallery usable: it installs the NuGet package provider when it
    /// is missing, then prints the PowerShell version so the probe has something to report.
    /// </summary>
    /// <remarks>
    /// Installing the provider is a one-time, per-user change, and it is the documented
    /// prerequisite for PowerShellGet. Doing it here, rather than on the first search, keeps
    /// every later command free of a side effect.
    /// </remarks>
    private const string EnsureGalleryBody = """
        if (-not (Get-PackageProvider -Name NuGet -ErrorAction SilentlyContinue)) {
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Force -Scope CurrentUser | Out-Null
        }
        Write-Output $PSVersionTable.PSVersion.ToString()
        """;

    private const string SearchBody = """
        $found = @()
        try {
            $found = @(Find-Module -Name {{SPEC}} -Repository PSGallery -ErrorAction Stop)
        } catch {
            if ($_.Exception.Message -notmatch 'No match was found') { throw }
            $found = @()
        }
        $rows = @($found | Select-Object -First {{LIMIT}} | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Version = $_.Version.ToString(); Repository = $_.Repository }
        })
        ConvertTo-Json -InputObject $rows -Compress
        """;

    private const string ListInstalledBody = """
        $rows = @(Get-InstalledModule -ErrorAction SilentlyContinue | ForEach-Object {
            [pscustomobject]@{ Name = $_.Name; Version = $_.Version.ToString(); Repository = $_.Repository }
        })
        ConvertTo-Json -InputObject $rows -Compress
        """;

    /// <summary>
    /// PowerShellGet has no command that lists available updates, so every installed module is
    /// looked up in one call and the two versions are compared.
    /// </summary>
    private const string ListUpgradableBody = """
        $installed = @(Get-InstalledModule -ErrorAction SilentlyContinue)
        $names = @($installed | Select-Object -ExpandProperty Name)
        $latest = @{}
        if ($names.Count -gt 0) {
            $found = @()
            try {
                $found = @(Find-Module -Name $names -Repository PSGallery -ErrorAction Stop)
            } catch {
                if ($_.Exception.Message -notmatch 'No match was found') { throw }
            }
            foreach ($module in $found) { $latest[$module.Name] = $module.Version.ToString() }
        }
        $rows = @(foreach ($module in $installed) {
            $available = $null
            if ($latest.ContainsKey($module.Name)) { $available = $latest[$module.Name] }
            [pscustomobject]@{ Name = $module.Name; Installed = $module.Version.ToString(); Available = $available }
        })
        ConvertTo-Json -InputObject $rows -Compress
        """;

    private const string DetailsBody = """
        $found = @(Find-Module -Name {{ID}} -Repository PSGallery -ErrorAction Stop | Select-Object -First 1)
        if ($found.Count -eq 0) { exit 0 }
        $module = $found[0]
        $published = $null
        if ($module.PublishedDate) { $published = $module.PublishedDate.ToString('yyyy-MM-dd') }
        $row = @([pscustomobject]@{
            Name = $module.Name
            Version = $module.Version.ToString()
            Author = ($module.Author -join ', ')
            CompanyName = ($module.CompanyName -join ', ')
            Description = $module.Description
            ProjectUri = $module.ProjectUri
            LicenseUri = $module.LicenseUri
            Repository = $module.Repository
            PublishedDate = $published
            Tags = ($module.Tags -join ', ')
            Copyright = $module.Copyright
        })
        ConvertTo-Json -InputObject $row -Compress
        """;

    private const string InstallBody = """
        Install-Module -Name {{ID}} -Force -Scope {{SCOPE}} -AllowClobber{{VERSION}}
        """;

    private const string UpdateBody = """
        Update-Module -Name {{ID}} -Force{{VERSION}}
        """;

    private const string UninstallBody = """
        Uninstall-Module -Name {{ID}} -Force -AllVersions
        """;

    public PowerShellGalleryProvider(IProcessRunner runner)
        : base(runner)
    {
    }

    public override string Id => ProviderId;

    public override string DisplayName => "PowerShell Gallery";

    protected override string ExecutableName => "powershell";

    /// <summary>Ten minutes: <c>Install-Module</c> downloads and unpacks a module tree.</summary>
    protected override TimeSpan WriteTimeout => TimeSpan.FromMinutes(10);

    protected override IReadOnlyList<string> VersionArguments => CommandArguments(EnsureGalleryBody);

    public override async Task<IReadOnlyList<Package>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var body = Render(SearchBody, ("{{SPEC}}", Quote($"*{query}*")), ("{{LIMIT}}", Math.Max(limit, 1).ToString()));
        var result = await RunReadAsync(CommandArguments(body), cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return [];
        }

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), element => new Package
        {
            ManagerId = ProviderId,
            Id = CliJson.String(element, "Name") ?? string.Empty,
            Name = CliJson.String(element, "Name") ?? string.Empty,
            AvailableVersion = CliJson.String(element, "Version"),
            Source = CliJson.String(element, "Repository") ?? SourceName,
        });
    }

    public override async Task<IReadOnlyList<Package>> ListInstalledAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(CommandArguments(ListInstalledBody), cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return [];
        }

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), element => new Package
        {
            ManagerId = ProviderId,
            Id = CliJson.String(element, "Name") ?? string.Empty,
            Name = CliJson.String(element, "Name") ?? string.Empty,
            InstalledVersion = CliJson.String(element, "Version"),
            Source = CliJson.String(element, "Repository") ?? SourceName,
        });
    }

    public override async Task<IReadOnlyList<Package>> ListUpgradableAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunReadAsync(CommandArguments(ListUpgradableBody), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return [];
        }

        var packages = new List<Package>();

        // Project to plain strings while the document is alive: the elements themselves are
        // backed by a document that CliJson.Select disposes before it returns.
        var rows = CliJson.Select(
            CliText.JoinLines(result.StandardOutput),
            element => (
                Name: CliJson.String(element, "Name"),
                Installed: CliJson.String(element, "Installed"),
                Available: CliJson.String(element, "Available")));

        foreach (var (name, installed, available) in rows)
        {
            // The comparison is done here rather than in the script so that one version rule
            // covers every manager.
            if (name is null || installed is null || available is null)
            {
                continue;
            }

            if (VersionComparison.Compare(available, installed) <= 0)
            {
                continue;
            }

            packages.Add(new Package
            {
                ManagerId = ProviderId,
                Id = name,
                Name = name,
                InstalledVersion = installed,
                UpdateVersion = available,
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

        var body = Render(DetailsBody, ("{{ID}}", Quote(package.Id)));
        var result = await RunReadAsync(CommandArguments(body), cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        return CliJson.Select(CliText.JoinLines(result.StandardOutput), element => ReadDetails(package, element))
            .FirstOrDefault();
    }

    protected override IReadOnlyList<string> BuildArguments(
        PackageOperation operation,
        Package package,
        InstallOptions options)
    {
        var version = string.IsNullOrWhiteSpace(options.Version)
            ? string.Empty
            : $" -RequiredVersion {Quote(options.Version!)}";

        // The gallery installs per user by default here. "machine" asks for AllUsers, which
        // needs an elevated process; a refusal is reported rather than worked around.
        var scope = string.Equals(options.Scope, "machine", StringComparison.OrdinalIgnoreCase)
            ? "AllUsers"
            : "CurrentUser";

        var body = operation switch
        {
            PackageOperation.Install => Render(
                InstallBody,
                ("{{ID}}", Quote(package.Id)),
                ("{{SCOPE}}", scope),
                ("{{VERSION}}", version)),
            PackageOperation.Update => Render(
                UpdateBody,
                ("{{ID}}", Quote(package.Id)),
                ("{{VERSION}}", version)),
            PackageOperation.Uninstall => Render(UninstallBody, ("{{ID}}", Quote(package.Id))),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown operation."),
        };

        return CommandArguments(body);
    }

    /// <summary>Reads the version the probe printed, allowing for any line before it.</summary>
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

    private static PackageDetails ReadDetails(Package package, System.Text.Json.JsonElement element)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in new[] { "Author", "CompanyName", "Repository", "PublishedDate", "Copyright", "ProjectUri", "LicenseUri" })
        {
            if (CliJson.String(element, name) is { } value)
            {
                fields[name] = value;
            }
        }

        var description = CliJson.String(element, "Description");
        var tags = CliJson.String(element, "Tags");

        return new PackageDetails
        {
            ManagerId = ProviderId,
            Id = package.Id,
            Name = CliJson.String(element, "Name") ?? package.Name,
            Version = CliJson.String(element, "Version"),
            Author = CliJson.String(element, "Author"),
            Publisher = CliJson.String(element, "CompanyName"),
            Description = description,
            Homepage = CliJson.String(element, "ProjectUri"),
            LicenseUrl = CliJson.String(element, "LicenseUri"),
            Copyright = CliJson.String(element, "Copyright"),
            ReleaseDate = CliJson.String(element, "PublishedDate"),
            Tags = string.IsNullOrWhiteSpace(tags)
                ? []
                : [.. tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)],
            Fields = fields,
        };
    }

    /// <summary>
    /// Wraps a script body so that any failure writes one readable line to the error stream and
    /// exits non-zero, which is what the base class turns into a result message.
    /// </summary>
    /// <remarks>
    /// An uncaught PowerShell error prints a multi-line block headed by the command and followed
    /// by its position, whose last line is not the message. Writing the exception message alone
    /// gives the user something they can act on.
    /// </remarks>
    private static string Wrap(string body) =>
        "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; " +
        "try { " + body + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";

    private static string EnsureLineWrap(string body) => body;

    private static IReadOnlyList<string> CommandArguments(string body) =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-Command",
        Wrap(body),
    ];

    /// <summary>Replaces placeholder tokens in a script template with prepared values.</summary>
    private static string Render(string template, params (string Token, string Value)[] tokens)
    {
        foreach (var (token, value) in tokens)
        {
            template = template.Replace(token, value, StringComparison.Ordinal);
        }

        return template;
    }

    /// <summary>
    /// Encloses a value as a PowerShell single-quoted string, which takes every character
    /// literally apart from the quote itself, and doubles that. Control characters are removed
    /// first, so a pasted newline cannot start a new statement.
    /// </summary>
    private static string Quote(string value)
    {
        var cleaned = new string([.. value.Where(character => !char.IsControl(character))]);
        return "'" + cleaned.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
