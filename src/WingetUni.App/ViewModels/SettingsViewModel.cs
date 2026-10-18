using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.App.Theming;
using WingetUni.Core.Models;
using WingetUni.Core.Services;

namespace WingetUni.App.ViewModels;

/// <summary>The Settings page: preferences, the package managers, and the package list tools.</summary>
public sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(AppSession session, IShell shell, IReadOnlyList<ManagerViewModel> managers)
        : base(session, shell, managers)
    {
        foreach (var manager in Managers)
        {
            manager.IsEnabled = Session.Options.IsManagerEnabled(manager.Id);
            manager.PropertyChanged += OnManagerPropertyChanged;
        }

        ExportCommand = new AsyncRelayCommand(ExportAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        ProbeCommand = new AsyncRelayCommand(ProbeAsync);
    }

    public ICommand ExportCommand { get; }

    public ICommand ImportCommand { get; }

    public ICommand ProbeCommand { get; }

    public string SettingsFilePath => Session.Settings.FilePath;

    public string? SettingsLoadError => Session.SettingsLoadError;

    public bool IsDarkTheme
    {
        get => !ThemeManager.IsLight;
        set { if (value) Shell.ApplyTheme(ThemeManager.Dark); }
    }

    public bool IsLightTheme
    {
        get => ThemeManager.IsLight;
        set { if (value) Shell.ApplyTheme(ThemeManager.Light); }
    }

    /// <summary>Re-reads the theme flags after the shell changed the palette.</summary>
    public void NotifyThemeChanged()
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    public bool CheckForUpdatesOnStartup
    {
        get => Session.Options.CheckForUpdatesOnStartup;
        set { if (Update(Session.Options with { CheckForUpdatesOnStartup = value }, value != Session.Options.CheckForUpdatesOnStartup)) OnPropertyChanged(); }
    }

    public bool NotifyWhenUpdatesAreAvailable
    {
        get => Session.Options.NotifyWhenUpdatesAreAvailable;
        set { if (Update(Session.Options with { NotifyWhenUpdatesAreAvailable = value }, value != Session.Options.NotifyWhenUpdatesAreAvailable)) OnPropertyChanged(); }
    }

    public bool SilentByDefault
    {
        get => Session.Options.SilentByDefault;
        set { if (Update(Session.Options with { SilentByDefault = value }, value != Session.Options.SilentByDefault)) OnPropertyChanged(); }
    }

    public int SearchResultLimit
    {
        get => Session.Options.SearchResultLimit;
        set
        {
            var limit = Math.Clamp(value, 10, 1000);
            if (Update(Session.Options with { SearchResultLimit = limit }, limit != Session.Options.SearchResultLimit))
            {
                OnPropertyChanged();
            }
        }
    }

    public bool IsDefaultScope
    {
        get => Session.Options.DefaultScope is null;
        set { if (value) SetScope(null); }
    }

    public bool IsUserScope
    {
        get => string.Equals(Session.Options.DefaultScope, "user", StringComparison.OrdinalIgnoreCase);
        set { if (value) SetScope("user"); }
    }

    public bool IsMachineScope
    {
        get => string.Equals(Session.Options.DefaultScope, "machine", StringComparison.OrdinalIgnoreCase);
        set { if (value) SetScope("machine"); }
    }

    public bool IsCloseToTray
    {
        get => Session.Options.MinimizeToTrayOnClose;
        set { if (value) SetCloseBehavior(true); }
    }

    public bool IsExitOnClose
    {
        get => !Session.Options.MinimizeToTrayOnClose;
        set { if (value) SetCloseBehavior(false); }
    }

    public string ManagerSummary
    {
        get
        {
            var available = Managers.Count(manager => manager.IsAvailable);
            return $"{available} of {Managers.Count} package managers found on this machine.";
        }
    }

    private void SetScope(string? scope)
    {
        if (string.Equals(Session.Options.DefaultScope, scope, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Persist(Session.Options with { DefaultScope = scope });
        OnPropertyChanged(nameof(IsDefaultScope));
        OnPropertyChanged(nameof(IsUserScope));
        OnPropertyChanged(nameof(IsMachineScope));
    }

    private void SetCloseBehavior(bool minimizeToTray)
    {
        if (Session.Options.MinimizeToTrayOnClose == minimizeToTray)
        {
            return;
        }

        Persist(Session.Options with { MinimizeToTrayOnClose = minimizeToTray });
        OnPropertyChanged(nameof(IsCloseToTray));
        OnPropertyChanged(nameof(IsExitOnClose));
    }

    private async Task ProbeAsync()
    {
        Shell.SetBusy(true, "Checking package managers\u2026");

        try
        {
            await Session.ProbeAsync();

            foreach (var manager in Managers)
            {
                manager.Refresh();
            }

            OnPropertyChanged(nameof(ManagerSummary));
            Shell.Report(ManagerSummary);
        }
        catch (Exception exception)
        {
            Session.Dialogs.ShowMessage("Checking managers failed", exception.Message);
        }
        finally
        {
            Shell.SetBusy(false);
        }
    }

    private async Task ExportAsync()
    {
        var path = Session.Dialogs.PickSaveFile(
            "Export package list",
            "Package list (*.json)|*.json|All files (*.*)|*.*",
            "packages.json");

        if (path is null)
        {
            return;
        }

        Shell.SetBusy(true, "Exporting installed packages\u2026");

        try
        {
            var outcome = await Session.Queries.ListInstalledAsync(Session.ActiveManagerIds);
            await Session.PackageLists.SaveAsync(path, outcome.Packages);

            var count = outcome.Packages.Count;
            Shell.Report($"Exported {count} package{(count == 1 ? string.Empty : "s")} to {path}.");
        }
        catch (Exception exception)
        {
            Session.Dialogs.ShowMessage("Export failed", exception.Message);
        }
        finally
        {
            Shell.SetBusy(false);
        }
    }

    private async Task ImportAsync()
    {
        var path = Session.Dialogs.PickOpenFile(
            "Import package list",
            "Package list (*.json)|*.json|All files (*.*)|*.*");

        if (path is null)
        {
            return;
        }

        PackageListFile file;

        try
        {
            file = await Session.PackageLists.LoadAsync(path);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Session.Dialogs.ShowMessage("Import failed", exception.Message);
            return;
        }

        var installable = file.Packages
            .Where(entry => Session.Managers.Find(entry.ManagerId) is { IsAvailable: true })
            .ToList();

        if (installable.Count == 0)
        {
            Session.Dialogs.ShowMessage(
                "Nothing to install",
                $"None of the {file.Packages.Count} packages in this list can be installed by a manager on this machine.");
            return;
        }

        if (!Session.Dialogs.Confirm(
                "Import package list",
                $"Install {installable.Count} package{(installable.Count == 1 ? string.Empty : "s")} recorded on {file.MachineName}?"))
        {
            return;
        }

        var options = DefaultInstallOptions();

        foreach (var entry in installable)
        {
            var package = new Package
            {
                ManagerId = entry.ManagerId,
                Id = entry.Id,
                Name = entry.Name ?? entry.Id,
                AvailableVersion = entry.Version,
            };

            Shell.Enqueue(PackageOperation.Install, ToViewModel(package), options);
        }
    }

    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ManagerViewModel.IsEnabled) || sender is not ManagerViewModel manager)
        {
            return;
        }

        Persist(Session.Options.WithManagerEnabled(manager.Id, manager.IsEnabled));
        OnPropertyChanged(nameof(ManagerSummary));
    }

    private bool Update(AppSettings settings, bool changed)
    {
        if (!changed)
        {
            return false;
        }

        Persist(settings);
        return true;
    }

    private void Persist(AppSettings settings) => _ = PersistAsync(settings);

    private async Task PersistAsync(AppSettings settings)
    {
        try
        {
            await Session.SaveSettingsAsync(settings);
        }
        catch (Exception exception)
        {
            Shell.Report($"Settings could not be saved: {exception.Message}");
        }
    }
}
