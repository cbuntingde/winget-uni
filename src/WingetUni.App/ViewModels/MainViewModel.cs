using System.Collections.ObjectModel;
using System.Windows.Input;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.App.Theming;
using WingetUni.Core.Models;
using WingetUni.Core.Services;

namespace WingetUni.App.ViewModels;

/// <summary>
/// The window's view model: the navigation rail, the status bar, the operation queue, and the
/// four pages. It is also the <see cref="IShell"/> the pages talk back to.
/// </summary>
public sealed class MainViewModel : ObservableObject, IShell, IDisposable
{
    private readonly AppSession _session;

    private object _currentPage;
    private string _currentSection = "installed";
    private bool _isBusy;
    private string _status = "Ready";
    private bool _showOperations;
    private bool _disposed;

    public MainViewModel(AppSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        foreach (var provider in session.Managers.Providers)
        {
            Managers.Add(new ManagerViewModel(provider));
        }

        Discover = new DiscoverViewModel(session, this, Managers);
        Installed = new InstalledViewModel(session, this, Managers);
        Updates = new UpdatesViewModel(session, this, Managers);
        Settings = new SettingsViewModel(session, this, Managers);

        _currentPage = Installed;

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string ?? "installed"));
        ApplyThemeCommand = new RelayCommand(parameter => ApplyTheme(parameter as string ?? ThemeManager.Dark));
        ToggleThemeCommand = new RelayCommand(() => ApplyTheme(ThemeManager.IsLight ? ThemeManager.Dark : ThemeManager.Light));
        CheckUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        CancelCurrentCommand = new RelayCommand(_ => _session.Operations.CancelCurrent());
        ClearOperationsCommand = new RelayCommand(ClearOperations);
        ToggleOperationsCommand = new RelayCommand(() => ShowOperations = !ShowOperations);
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));

        _session.Operations.RecordChanged += OnRecordChanged;
        _session.Operations.QueueChanged += OnQueueChanged;
    }

    /// <summary>Raised after an update check, carrying how many updates were found.</summary>
    public event EventHandler<int>? UpdatesChecked;

    /// <summary>Raised when the user asks to close the application, from the rail or the tray icon.</summary>
    public event EventHandler? ExitRequested;

    public AppSession Session => _session;

    public DiscoverViewModel Discover { get; }

    public InstalledViewModel Installed { get; }

    public UpdatesViewModel Updates { get; }

    public SettingsViewModel Settings { get; }

    public ObservableCollection<ManagerViewModel> Managers { get; } = [];

    public ObservableCollection<OperationViewModel> Operations { get; } = [];

    public ICommand NavigateCommand { get; }

    public ICommand ApplyThemeCommand { get; }

    public ICommand ToggleThemeCommand { get; }

    public ICommand CheckUpdatesCommand { get; }

    public ICommand CancelCurrentCommand { get; }

    public ICommand ClearOperationsCommand { get; }

    public ICommand ToggleOperationsCommand { get; }

    public ICommand ExitCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentSection
    {
        get => _currentSection;
        private set => SetProperty(ref _currentSection, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool ShowOperations
    {
        get => _showOperations;
        set => SetProperty(ref _showOperations, value);
    }

    public bool HasOperations => Operations.Count > 0;

    public bool IsQueueRunning => _session.Operations.IsRunning;

    public string ThemeLabel => ThemeManager.IsLight ? "Light" : "Dark";

    public string SectionTitle => CurrentSection switch
    {
        "installed" => "Installed packages",
        "updates" => "Available updates",
        "settings" => "Settings",
        _ => "Discover packages",
    };

    public string UpdateSummary => Updates.Packages.Count == 0
        ? "No updates found"
        : $"{Updates.Packages.Count} update{(Updates.Packages.Count == 1 ? string.Empty : "s")} available";

    /// <summary>Probes the managers, loads settings, and shows the first lists.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _session.InitializeAsync();
        }
        catch (Exception exception)
        {
            Status = $"Settings could not be loaded: {exception.Message}";
        }

        foreach (var manager in Managers)
        {
            manager.IsEnabled = _session.Options.IsManagerEnabled(manager.Id);
        }

        ThemeManager.Apply(_session.Options.Theme);
        OnPropertyChanged(nameof(ThemeLabel));

        SetBusy(true, "Looking for package managers\u2026");

        try
        {
            await _session.ProbeAsync();
        }
        catch (Exception exception)
        {
            Report($"Checking for package managers failed: {exception.Message}");
        }

        foreach (var manager in Managers)
        {
            manager.Refresh();
        }

        SetBusy(false, DescribeManagers());

        if (_session.SettingsLoadError is { } settingsError)
        {
            Report(settingsError);
        }

        Installed.Activate();
        Updates.Activate();

        await Installed.ReloadAsync(userInitiated: false);

        if (_session.Options.CheckForUpdatesOnStartup)
        {
            await CheckForUpdatesAsync();
        }
        else
        {
            Navigate("installed");
        }
    }

    public void Navigate(string section)
    {
        CurrentSection = section.ToLowerInvariant();
        OnPropertyChanged(nameof(SectionTitle));

        CurrentPage = CurrentSection switch
        {
            "installed" => Installed,
            "updates" => Updates,
            "settings" => Settings,
            _ => Discover,
        };
    }

    public void ApplyTheme(string theme)
    {
        ThemeManager.Apply(theme);
        OnPropertyChanged(nameof(ThemeLabel));
        Settings.NotifyThemeChanged();

        _ = PersistAsync(_session.Options with { Theme = ThemeManager.Current });
    }

    public void SetBusy(bool busy, string? message = null)
    {
        IsBusy = busy;

        if (message is not null)
        {
            Status = message;
        }
    }

    public void Report(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            Status = message;
        }
    }

    public void Enqueue(PackageOperation operation, PackageViewModel package, InstallOptions options)
    {
        ArgumentNullException.ThrowIfNull(package);

        var provider = _session.Managers.Find(package.ManagerId);
        if (provider is null)
        {
            Report($"No package manager handles {package.ManagerId}.");
            return;
        }

        if (!provider.IsAvailable)
        {
            Report($"{provider.DisplayName} is not available: {provider.UnavailableReason}");
            return;
        }

        _session.Operations.Enqueue(
        [
            new PackageOperationRequest
            {
                Provider = provider,
                Operation = operation,
                Package = package.Model,
                Options = options,
            },
        ]);

        ShowOperations = true;
        Report($"Queued: {Describe(operation)} {package.Name}.");
    }

    public async Task ShowDetailsAsync(PackageViewModel package)
    {
        SetBusy(true, $"Reading details for {package.Name}\u2026");

        try
        {
            var provider = _session.Managers.Find(package.ManagerId);
            if (provider is null)
            {
                _session.Dialogs.ShowMessage("Details", $"No package manager handles {package.ManagerId}.");
                return;
            }

            var details = await provider.GetDetailsAsync(package.Model);
            if (details is null)
            {
                _session.Dialogs.ShowMessage("Details", $"{provider.DisplayName} did not describe {package.Name}.");
                return;
            }

            _session.Dialogs.ShowDetails(new PackageDetailsViewModel(details, provider.DisplayName));
        }
        catch (Exception exception)
        {
            _session.Dialogs.ShowMessage("Details failed", exception.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task CheckForUpdatesAsync()
    {
        SetBusy(true, "Checking for updates\u2026");

        try
        {
            await Updates.ReloadAsync(userInitiated: false);
        }
        finally
        {
            SetBusy(false, UpdateSummary);
        }

        OnPropertyChanged(nameof(UpdateSummary));
        UpdatesChecked?.Invoke(this, Updates.Packages.Count);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Operations.RecordChanged -= OnRecordChanged;
        _session.Operations.QueueChanged -= OnQueueChanged;
        Installed.Deactivate();
        Updates.Deactivate();
    }

    private void OnRecordChanged(object? sender, OperationRecord record) =>
        _session.Dispatcher.Post(() => ApplyRecord(record));

    private void OnQueueChanged(object? sender, EventArgs e) =>
        _session.Dispatcher.Post(() => OnPropertyChanged(nameof(IsQueueRunning)));

    private void ApplyRecord(OperationRecord record)
    {
        var existing = Operations.FirstOrDefault(operation => operation.Id == record.Id);
        if (existing is null)
        {
            Operations.Insert(0, new OperationViewModel(record));
        }
        else
        {
            existing.Update(record);
        }

        OnPropertyChanged(nameof(HasOperations));

        if (!record.IsFinished)
        {
            return;
        }

        if (record.State == OperationState.Succeeded)
        {
            // The installed set has changed; the pages reload from the manager rather than
            // assuming what the operation did.
            _session.RaiseLibraryChanged();
        }

        Report(record.Message ?? $"{record.Title} {StateWord(record.State)}.");
    }

    private void ClearOperations()
    {
        Operations.Clear();
        OnPropertyChanged(nameof(HasOperations));
    }

    private async Task PersistAsync(AppSettings settings)
    {
        try
        {
            await _session.SaveSettingsAsync(settings);
        }
        catch (Exception exception)
        {
            Report($"Settings could not be saved: {exception.Message}");
        }
    }

    private string DescribeManagers()
    {
        var available = Managers.Count(manager => manager.IsAvailable);
        return available == 0
            ? "No package managers were found. Open Settings to see why."
            : $"{available} package manager{(available == 1 ? string.Empty : "s")} ready.";
    }

    private static string Describe(PackageOperation operation) => operation switch
    {
        PackageOperation.Install => "install",
        PackageOperation.Update => "update",
        PackageOperation.Uninstall => "remove",
        _ => "process",
    };

    private static string StateWord(OperationState state) => state switch
    {
        OperationState.Pending => "is waiting",
        OperationState.Running => "is running",
        OperationState.Succeeded => "finished.",
        OperationState.Failed => "failed.",
        OperationState.Canceled => "was cancelled.",
        _ => "finished.",
    };
}
