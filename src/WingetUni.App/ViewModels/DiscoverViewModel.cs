using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.Core.Models;
using WingetUni.Core.Services;
using WingetUni.Providers.WinGet;

namespace WingetUni.App.ViewModels;

/// <summary>
/// The Discover page: search one package manager, or all of them, and install from the results.
/// </summary>
/// <remarks>
/// Installing is one click. The package is installed the way the manager itself would install
/// it, with no version or location chosen in advance, because that is what a user typing a
/// package name expects. Anyone who needs to pin a version or pick a scope can open the options
/// dialog from the row instead.
///
/// Searching starts at the primary manager rather than every manager, for the same reason a
/// person looking for a Winget package does not want results from npm: each manager costs its own
/// process, and the one that answers the question is almost always the primary one. The choice is
/// remembered, and "All managers" is there for the times it is genuinely needed.
/// </remarks>
public sealed class DiscoverViewModel : PageViewModel
{
    /// <summary>How many rows one page of results shows.</summary>
    public const int PageSize = 25;

    /// <summary>The choice that searches every available manager at once.</summary>
    public static readonly ManagerChoice AllManagersChoice = new(null, "All managers");

    private readonly List<PackageViewModel> _matches = [];

    private string _query = string.Empty;
    private string? _status;
    private int _pageIndex;
    private ManagerChoice? _selectedManager;

    /// <summary>
    /// Set while the choice list is being rebuilt. The rebuild puts the selection back where it
    /// belongs rather than recording a change the user did not make, so nothing is persisted
    /// from it.
    /// </summary>
    private bool _rebuildingChoices;

    public DiscoverViewModel(AppSession session, IShell shell, IReadOnlyList<ManagerViewModel> managers)
        : base(session, shell, managers)
    {
        SearchCommand = new AsyncRelayCommand(SearchAsync);
        InstallCommand = new AsyncRelayCommand(InstallAsync);
        OptionsCommand = new AsyncRelayCommand(OptionsAsync);
        DetailsCommand = new AsyncRelayCommand(DetailsAsync);
        NextPageCommand = new RelayCommand(NextPage, () => CanGoNext);
        PreviousPageCommand = new RelayCommand(PreviousPage, () => CanGoPrevious);

        // A manager becomes available only once it has been probed, and can be switched off in
        // settings at any time, so the list of managers that can be searched is rebuilt whenever
        // either changes rather than being fixed at construction.
        foreach (var manager in Managers)
        {
            manager.PropertyChanged += OnManagerPropertyChanged;
        }

        RefreshManagerChoices();

        Status = "Type a search term, choose a package manager, and press Search.";
    }

    /// <summary>One package manager, or every one of them, as offered by the dropdown.</summary>
    /// <param name="Id">The manager, or null for "All managers".</param>
    /// <param name="DisplayName">The text shown in the dropdown.</param>
    public sealed record ManagerChoice(string? Id, string DisplayName);

    /// <summary>The managers that can be searched, and the option that searches all of them.</summary>
    public ObservableCollection<ManagerChoice> ManagerChoices { get; } = [];

    /// <summary>The rows of the page currently being shown.</summary>
    public ObservableCollection<PackageViewModel> Results { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public ICommand SearchCommand { get; }

    public ICommand InstallCommand { get; }

    public ICommand OptionsCommand { get; }

    public ICommand DetailsCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public string Query
    {
        get => _query;
        set => SetProperty(ref _query, value);
    }

    public ManagerChoice? SelectedManager
    {
        get => _selectedManager;
        set
        {
            // The field is never left unset: the dropdown always has a choice, and the null that
            // reaches this property while the list is rebuilt is not a choice the user made.
            if (value is null || !SetProperty(ref _selectedManager, value))
            {
                return;
            }

            if (!_rebuildingChoices)
            {
                PersistManagerChoice(value.Id);
            }
        }
    }

    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool HasResults => _matches.Count > 0;

    public bool HasErrors => Errors.Count > 0;

    public bool HasPages => PageCount > 1;

    public bool CanGoNext => _pageIndex + 1 < PageCount;

    public bool CanGoPrevious => _pageIndex > 0;

    public string PageSummary => PageCount == 0 ? string.Empty : $"Page {_pageIndex + 1} of {PageCount}";

    private int PageCount => _matches.Count == 0 ? 0 : (_matches.Count + PageSize - 1) / PageSize;

    protected override void ConfigureActions(PackageViewModel package)
    {
        package.Actions.Add(new PackageActionViewModel("Install", InstallCommand, package, isPrimary: true));
        package.Actions.Add(new PackageActionViewModel("Options\u2026", OptionsCommand, package));
        package.Actions.Add(new PackageActionViewModel("Details", DetailsCommand, package));
    }

    public async Task SearchAsync()
    {
        var query = _query.Trim();
        if (query.Length == 0)
        {
            _matches.Clear();
            Results.Clear();
            Errors.Clear();
            _pageIndex = 0;
            Status = "Type a search term, choose a package manager, and press Search.";
            RaiseResultFlags();
            return;
        }

        var managerIds = SelectedManagerIds();
        if (managerIds.Count == 0)
        {
            Status = "No package manager can be searched. Check that one is switched on in Settings.";
            return;
        }

        Shell.SetBusy(true, $"Searching for \u201c{query}\u201d in {ScopePhrase}\u2026");

        try
        {
            var outcome = await RunSearchAsync(query, Session.Options.SearchResultLimit, managerIds);

            _matches.Clear();
            _matches.AddRange(outcome.Packages.Select(ToViewModel));
            Errors.ReplaceWith(outcome.Errors);
            _pageIndex = 0;
            ShowPage();

            Status = _matches.Count == 0
                ? $"No packages matched {ScopePhrase}."
                : $"{_matches.Count} package{(_matches.Count == 1 ? string.Empty : "s")} found {ScopePhrase}.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            Shell.SetBusy(false, Status);
            RaiseResultFlags();
        }
    }

    private Task InstallAsync(object? parameter)
    {
        if (parameter is not PackageViewModel package)
        {
            return Task.CompletedTask;
        }

        Shell.Enqueue(PackageOperation.Install, package, DefaultInstallOptions());
        return Task.CompletedTask;
    }

    private Task OptionsAsync(object? parameter)
    {
        if (parameter is not PackageViewModel package)
        {
            return Task.CompletedTask;
        }

        var options = Session.Dialogs.ShowInstallOptions(
            PackageOperation.Install,
            package.ManagerName,
            package.Model,
            DefaultInstallOptions());

        if (options is not null)
        {
            Shell.Enqueue(PackageOperation.Install, package, options);
        }

        return Task.CompletedTask;
    }

    private Task DetailsAsync(object? parameter) =>
        parameter is PackageViewModel package ? Shell.ShowDetailsAsync(package) : Task.CompletedTask;

    /// <summary>Runs a search, reusing the answer when the same search was made recently.</summary>
    private async Task<PackageQueryOutcome> RunSearchAsync(string query, int limit, IReadOnlyList<string> managerIds)
    {
        var key = SearchResultCache.Key(query, limit, managerIds);
        if (Session.Searches.TryGet(key, out var cached))
        {
            return cached;
        }

        var outcome = await Session.Queries.SearchAsync(query, limit, managerIds);
        Session.Searches.Set(key, outcome);
        return outcome;
    }

    /// <summary>
    /// The managers to ask. Only managers that are installed and switched on are ever queried,
    /// which the choice list already reflects; this narrows once more so that a manager switched
    /// off in settings between two searches is not asked anyway.
    /// </summary>
    private IReadOnlyList<string> SelectedManagerIds()
    {
        var active = Session.ActiveManagerIds;

        return SelectedManager?.Id is { } chosen
            ? [.. active.Where(id => string.Equals(id, chosen, StringComparison.Ordinal))]
            : active;
    }

    /// <summary>How the current choice reads at the end of a sentence, for example "in WinGet".</summary>
    private string ScopePhrase => SelectedManager?.Id is { } chosen
        ? $"in {ManagerName(chosen)}"
        : "across all managers";

    /// <summary>
    /// Rebuilds the list of searchable managers. A manager that is not installed or that the user
    /// switched off cannot answer, so it is not offered.
    /// </summary>
    private void RefreshManagerChoices()
    {
        var wanted = ResolveSavedManagerId();

        var desired = new List<ManagerChoice> { AllManagersChoice };
        desired.AddRange(Managers
            .Where(manager => manager.IsAvailable && manager.IsEnabled)
            .Select(manager => new ManagerChoice(manager.Id, manager.DisplayName)));

        // Reconciled in place rather than cleared and refilled. Clearing the list would make the
        // dropdown drop its selection in the middle of the rebuild, and a selection that the
        // dropdown lost is not a choice the user made.
        for (var index = 0; index < desired.Count; index++)
        {
            if (index >= ManagerChoices.Count)
            {
                ManagerChoices.Add(desired[index]);
            }
            else if (ManagerChoices[index] != desired[index])
            {
                ManagerChoices[index] = desired[index];
            }
        }

        while (ManagerChoices.Count > desired.Count)
        {
            ManagerChoices.RemoveAt(ManagerChoices.Count - 1);
        }

        _rebuildingChoices = true;

        try
        {
            SelectedManager =
                ManagerChoices.FirstOrDefault(choice => string.Equals(choice.Id, wanted, StringComparison.Ordinal))
                ?? ManagerChoices.FirstOrDefault(choice => string.Equals(choice.Id, DefaultManagerId(), StringComparison.Ordinal))
                ?? AllManagersChoice;
        }
        finally
        {
            _rebuildingChoices = false;
        }
    }

    /// <summary>The manager the current settings ask for, or null for all of them.</summary>
    private string? ResolveSavedManagerId() =>
        Session.Options.ResolveDiscoverManagerId(DefaultManagerId());

    /// <summary>
    /// Where a search starts when the user has never chosen: WinGet, the manager this application
    /// is built around, or otherwise the first manager able to answer.
    /// </summary>
    private string? DefaultManagerId() =>
        Managers.FirstOrDefault(manager =>
            manager.IsAvailable && manager.IsEnabled &&
            string.Equals(manager.Id, WinGetProvider.ProviderId, StringComparison.Ordinal))?.Id
        ?? Managers.FirstOrDefault(manager => manager.IsAvailable && manager.IsEnabled)?.Id;

    private void PersistManagerChoice(string? managerId)
    {
        _ = PersistAsync(Session.Options with { DiscoverManagerId = managerId ?? AppSettings.AllManagers });
    }

    private async Task PersistAsync(AppSettings settings)
    {
        try
        {
            await Session.SaveSettingsAsync(settings);
        }
        catch (Exception exception)
        {
            Status = $"Settings could not be saved: {exception.Message}";
        }
    }

    private void OnManagerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ManagerViewModel.IsAvailable) or nameof(ManagerViewModel.IsEnabled))
        {
            RefreshManagerChoices();
        }
    }

    private void ShowPage() =>
        Results.ReplaceWith(_matches.Skip(_pageIndex * PageSize).Take(PageSize));

    private void NextPage()
    {
        if (!CanGoNext)
        {
            return;
        }

        _pageIndex++;
        ShowPage();
        RaisePageFlags();
    }

    private void PreviousPage()
    {
        if (!CanGoPrevious)
        {
            return;
        }

        _pageIndex--;
        ShowPage();
        RaisePageFlags();
    }

    private void RaiseResultFlags()
    {
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasErrors));
        RaisePageFlags();
    }

    private void RaisePageFlags()
    {
        OnPropertyChanged(nameof(HasPages));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(PageSummary));
        CommandManager.InvalidateRequerySuggested();
    }
}
