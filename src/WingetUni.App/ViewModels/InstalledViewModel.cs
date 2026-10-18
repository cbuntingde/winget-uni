using System.Windows.Input;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.Core.Models;
using WingetUni.Core.Services;

namespace WingetUni.App.ViewModels;

/// <summary>The Installed page: what is on this machine, with update and removal actions.</summary>
public sealed class InstalledViewModel : ReloadablePageViewModel
{
    public InstalledViewModel(AppSession session, IShell shell, IReadOnlyList<ManagerViewModel> managers)
        : base(session, shell, managers)
    {
        UpdateCommand = new AsyncRelayCommand(UpdateAsync);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync);
        DetailsCommand = new AsyncRelayCommand(DetailsAsync);
    }

    public ICommand UpdateCommand { get; }

    public ICommand UninstallCommand { get; }

    public ICommand DetailsCommand { get; }

    protected override string LoadingMessage => "Reading installed packages\u2026";

    protected override Task<PackageQueryOutcome> QueryAsync(CancellationToken cancellationToken) =>
        Session.Queries.ListInstalledAsync(QueryManagerIds, cancellationToken);

    protected override void ConfigureActions(PackageViewModel package)
    {
        if (package.HasUpdate)
        {
            package.Actions.Add(new PackageActionViewModel("Update", UpdateCommand, package, isPrimary: true));
        }

        package.Actions.Add(new PackageActionViewModel("Details", DetailsCommand, package));
        package.Actions.Add(new PackageActionViewModel("Remove", UninstallCommand, package, isDanger: true));
    }

    private Task UpdateAsync(object? parameter)
    {
        if (parameter is not PackageViewModel package)
        {
            return Task.CompletedTask;
        }

        // Updating to the newest version is the obvious meaning of the row's Update button, so it
        // is not confirmed. Removing is, because it cannot be undone.
        Shell.Enqueue(PackageOperation.Update, package, DefaultInstallOptions());
        return Task.CompletedTask;
    }

    private Task UninstallAsync(object? parameter)
    {
        if (parameter is not PackageViewModel package)
        {
            return Task.CompletedTask;
        }

        if (!Session.Dialogs.Confirm(
                "Remove package",
                $"Remove {package.Name} ({package.Id}) with {package.ManagerName}? This cannot be undone."))
        {
            return Task.CompletedTask;
        }

        Shell.Enqueue(PackageOperation.Uninstall, package, DefaultInstallOptions());
        return Task.CompletedTask;
    }

    private Task DetailsAsync(object? parameter) =>
        parameter is PackageViewModel package ? Shell.ShowDetailsAsync(package) : Task.CompletedTask;
}
