using System.Windows.Input;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.Core.Models;
using WingetUni.Core.Services;

namespace WingetUni.App.ViewModels;

/// <summary>The Updates page: everything a manager offers a newer version of.</summary>
public sealed class UpdatesViewModel : ReloadablePageViewModel
{
    public UpdatesViewModel(AppSession session, IShell shell, IReadOnlyList<ManagerViewModel> managers)
        : base(session, shell, managers)
    {
        UpdateCommand = new AsyncRelayCommand(UpdateAsync);
        UpdateSelectedCommand = new AsyncRelayCommand(
            UpdateSelectedAsync,
            () => Packages.Any(package => package.IsSelected));
        SelectAllCommand = new RelayCommand(() => SetSelection(true));
        ClearSelectionCommand = new RelayCommand(() => SetSelection(false));
    }

    public ICommand UpdateCommand { get; }

    public ICommand UpdateSelectedCommand { get; }

    public ICommand SelectAllCommand { get; }

    public ICommand ClearSelectionCommand { get; }

    protected override string LoadingMessage => "Checking for updates\u2026";

    protected override Task<PackageQueryOutcome> QueryAsync(CancellationToken cancellationToken) =>
        Session.Queries.ListUpgradableAsync(QueryManagerIds, cancellationToken);

    protected override void ConfigureActions(PackageViewModel package) =>
        package.Actions.Add(new PackageActionViewModel("Update", UpdateCommand, package, isPrimary: true));

    protected override void OnLoaded()
    {
        // Everything starts ticked so that the common case, taking every update, is one click.
        foreach (var package in Packages)
        {
            package.IsSelected = true;
        }
    }

    private Task UpdateAsync(object? parameter)
    {
        if (parameter is PackageViewModel package)
        {
            Shell.Enqueue(PackageOperation.Update, package, DefaultInstallOptions());
        }

        return Task.CompletedTask;
    }

    private Task UpdateSelectedAsync()
    {
        var selected = Packages.Where(package => package.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!Session.Dialogs.Confirm(
                "Update packages",
                $"Update {selected.Count} selected package{(selected.Count == 1 ? string.Empty : "s")}?"))
        {
            return Task.CompletedTask;
        }

        foreach (var package in selected)
        {
            Shell.Enqueue(PackageOperation.Update, package, DefaultInstallOptions());
            package.IsSelected = false;
        }

        return Task.CompletedTask;
    }

    private void SetSelection(bool selected)
    {
        foreach (var package in Packages)
        {
            package.IsSelected = selected;
        }

        CommandManager.InvalidateRequerySuggested();
    }
}
