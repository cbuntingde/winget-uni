using System.Text.Json;

namespace WingetUni.Core.Services;

/// <summary>Reads and writes <see cref="AppSettings"/> as a JSON file.</summary>
/// <remarks>
/// A settings file is the one piece of state a user cannot recover from, so a damaged file
/// falls back to defaults and reports why instead of preventing startup, and writes go to a
/// temporary file that is then moved into place so an interrupted write cannot truncate the
/// settings that are already there.
/// </remarks>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public SettingsService(string? directory = null)
    {
        var root = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WingetUni");

        FilePath = Path.Combine(root, "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Current { get; private set; } = new();

    /// <summary>Why the last load fell back to defaults, or null when it succeeded.</summary>
    public string? LastLoadError { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        LastLoadError = null;

        if (!File.Exists(FilePath))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            Current = loaded ?? new AppSettings();

            if (loaded is null)
            {
                LastLoadError = "The settings file was empty, so defaults were used.";
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // Unreadable settings must not stop the application from starting.
            Current = new AppSettings();
            LastLoadError = $"Settings could not be read ({exception.Message}), so defaults were used.";
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Current = settings;

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = FilePath + ".tmp";

            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer
                    .SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
