using System.Text.Json;
using WingetUni.Core.Models;

namespace WingetUni.Core.Services;

/// <summary>One package in an exported list.</summary>
public sealed record PackageListEntry
{
    public required string ManagerId { get; init; }

    public required string Id { get; init; }

    public string? Name { get; init; }

    public string? Version { get; init; }
}

/// <summary>The contents of an exported package list file.</summary>
public sealed record PackageListFile
{
    public required int FormatVersion { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required string MachineName { get; init; }

    public required IReadOnlyList<PackageListEntry> Packages { get; init; }
}

/// <summary>Exports installed packages to a file and reads them back on another machine.</summary>
/// <remarks>
/// The file records the format version and the machine it came from so that a list written by a
/// newer version of the application is rejected with an explanation rather than being
/// misinterpreted.
/// </remarks>
public sealed class PackageListService
{
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Largest list file accepted for reading. A package list is plain text of a few
    /// kilobytes; anything above this is not a list the application wrote.
    /// </summary>
    public const long MaxFileBytes = 10_000_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public async Task SaveAsync(
        string path,
        IEnumerable<Package> packages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(packages);

        var file = new PackageListFile
        {
            FormatVersion = CurrentFormatVersion,
            ExportedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            Packages =
            [
                .. packages.Select(package => new PackageListEntry
                {
                    ManagerId = package.ManagerId,
                    Id = package.Id,
                    Name = package.Name,
                    Version = package.InstalledVersion ?? package.AvailableVersion,
                }),
            ],
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temporary file and move it into place so an interrupted export
        // cannot truncate the list that is already there.
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, file, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>
    /// Reads a package list.
    /// </summary>
    /// <exception cref="InvalidDataException">
    /// The file is not a package list, or was written by an unsupported format version.
    /// </exception>
    public async Task<PackageListFile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new InvalidDataException($"'{path}' does not exist.");
        }

        if (new FileInfo(path).Length > MaxFileBytes)
        {
            throw new InvalidDataException($"'{path}' is larger than {MaxFileBytes} bytes and is not a package list.");
        }

        PackageListFile? file;

        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer
                .DeserializeAsync<PackageListFile>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"'{path}' is not a package list: {exception.Message}", exception);
        }

        if (file is null)
        {
            throw new InvalidDataException($"'{path}' is empty.");
        }

        if (file.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"'{path}' uses package list format {file.FormatVersion}, but this version of the application reads format {CurrentFormatVersion}.");
        }

        return file;
    }
}
