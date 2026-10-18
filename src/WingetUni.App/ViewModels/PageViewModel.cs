using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.Core.Models;

namespace WingetUni.App.ViewModels;

/// <summary>Shared plumbing for the pages that show a list of packages.</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(AppSession session, IShell shell, IReadOnlyList<ManagerViewModel> managers)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Shell = shell ?? throw new ArgumentNullException(nameof(shell));
        Managers = managers ?? throw new ArgumentNullException(nameof(managers));
    }

    protected AppSession Session { get; }

    protected IShell Shell { get; }

    public IReadOnlyList<ManagerViewModel> Managers { get; }

    /// <summary>The managers a query on this page may talk to.</summary>
    protected IReadOnlyList<string> QueryManagerIds =>
    [
        .. Managers
            .Where(manager => manager.IsAvailable && manager.IsEnabled)
            .Select(manager => manager.Id),
    ];

    /// <summary>The install options a dialog starts from, taken from the saved preferences.</summary>
    protected InstallOptions DefaultInstallOptions() => new()
    {
        Silent = Session.Options.SilentByDefault,
        Scope = Session.Options.DefaultScope,
    };

    protected string ManagerName(string managerId) =>
        Managers.FirstOrDefault(manager => string.Equals(manager.Id, managerId, StringComparison.Ordinal))?.DisplayName
            ?? managerId;

    protected PackageViewModel ToViewModel(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);

        var viewModel = new PackageViewModel(package, ManagerName(package.ManagerId));
        ConfigureActions(viewModel);
        return viewModel;
    }

    /// <summary>
    /// Gives a freshly built row the buttons this page offers for it. The default adds none, so a
    /// page that only lists packages needs no code here.
    /// </summary>
    protected virtual void ConfigureActions(PackageViewModel package)
    {
    }
}
