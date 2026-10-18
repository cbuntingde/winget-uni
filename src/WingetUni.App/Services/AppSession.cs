using WingetUni.App.Infrastructure;
using WingetUni.Core.Abstractions;
using WingetUni.Core.Services;
using WingetUni.Providers;

namespace WingetUni.App.Services;

/// <summary>
/// The pieces every part of the interface shares: the package managers, the services built on
/// them, the user's settings, and the notification that something changed.
/// </summary>
/// <remarks>
/// This is created once at startup and handed to the view models. It owns the objects with a
/// lifetime as long as the application, which keeps the view models free of construction detail.
/// </remarks>
public sealed class AppSession : IDisposable
{
    private bool _disposed;

    public AppSession(
        IProcessRunner runner,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        SettingsService settings,
        PackageListService packageLists)
    {
        ArgumentNullException.ThrowIfNull(runner);

        Managers = ProviderCatalog.CreateRegistry(runner);
        Queries = new PackageQueryService(Managers);
        Operations = new PackageOperationQueue();
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        PackageLists = packageLists ?? throw new ArgumentNullException(nameof(packageLists));
    }

    public PackageManagerRegistry Managers { get; }

    public PackageQueryService Queries { get; }

    public PackageOperationQueue Operations { get; }

    public IDialogService Dialogs { get; }

    public IUiDispatcher Dispatcher { get; }

    public SettingsService Settings { get; }

    public PackageListService PackageLists { get; }

    /// <summary>Recent searches, so repeating one does not run the managers again.</summary>
    public SearchResultCache Searches { get; } = new();

    public AppSettings Options { get; private set; } = new();

    /// <summary>Why the settings file could not be read, or null when it loaded cleanly.</summary>
    public string? SettingsLoadError { get; private set; }

    /// <summary>Raised after the settings are changed and saved.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>Raised when installed packages may have changed, so the lists should reload.</summary>
    public event EventHandler? LibraryChanged;

    /// <summary>
    /// The managers a query should talk to: present on the machine and not switched off by the
    /// user. A manager that is unavailable is never queried.
    /// </summary>
    public IReadOnlyList<string> ActiveManagerIds =>
    [
        .. Managers.Available
            .Where(manager => Options.IsManagerEnabled(manager.Id))
            .Select(manager => manager.Id),
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        Options = Settings.Current;
        SettingsLoadError = Settings.LastLoadError;
    }

    /// <summary>Checks every manager for the tool it drives.</summary>
    public Task ProbeAsync(CancellationToken cancellationToken = default) =>
        Managers.ProbeAllAsync(cancellationToken);

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Options = settings;
        await Settings.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseLibraryChanged()
    {
        // A search result records whether each package is already installed, so a change to the
        // installed set makes every remembered search stale.
        Searches.Clear();
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Operations.Dispose();
    }
}
