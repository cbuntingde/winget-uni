using System.Collections.ObjectModel;
using System.Windows.Input;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.Core.Services;

namespace WingetUni.App.ViewModels;

/// <summary>
/// A page whose list is filled by one query and refreshed when the installed set changes.
/// </summary>
/// <remarks>
/// Subscribing to <see cref="AppSession.LibraryChanged"/> is what keeps the Installed and Updates
/// pages honest after an operation: the queue raises it when something finishes, and the page
/// reloads from the manager rather than guessing what changed.
/// </remarks>
public abstract class ReloadablePageViewModel : PageViewModel
{
    private bool _isBusy;
    private string? _status;
    private bool _subscribed;

    protected ReloadablePageViewModel(AppSession session, IShell shell, IReadOnlyList<ManagerViewModel> managers)
        : base(session, shell, managers)
    {
        RefreshCommand = new AsyncRelayCommand(() => ReloadAsync(userInitiated: true));
    }

    public ObservableCollection<PackageViewModel> Packages { get; } = [];

    public ObservableCollection<string> Errors { get; } = [];

    public ICommand RefreshCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsEmpty => Packages.Count == 0 && !IsBusy;

    public bool HasErrors => Errors.Count > 0;

    protected abstract string LoadingMessage { get; }

    protected abstract Task<PackageQueryOutcome> QueryAsync(CancellationToken cancellationToken);

    /// <summary>Called right after a reload has filled <see cref="Packages"/>.</summary>
    protected virtual void OnLoaded()
    {
    }

    /// <summary>Starts reloading whenever another page changes the installed set.</summary>
    public void Activate()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        Session.LibraryChanged += OnLibraryChanged;
    }

    public void Deactivate()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        Session.LibraryChanged -= OnLibraryChanged;
    }

    public async Task ReloadAsync(bool userInitiated)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        if (userInitiated)
        {
            Shell.SetBusy(true, LoadingMessage);
        }

        try
        {
            var outcome = await QueryAsync(CancellationToken.None).ConfigureAwait(true);

            Packages.ReplaceWith(outcome.Packages.Select(ToViewModel));
            Errors.ReplaceWith(outcome.Errors);
            OnLoaded();
            Status = Describe(outcome);
        }
        catch (Exception exception)
        {
            Status = exception.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasErrors));

            if (userInitiated)
            {
                Shell.SetBusy(false, Status);
            }
            else if (Status is not null)
            {
                Shell.Report(Status);
            }
        }
    }

    private void OnLibraryChanged(object? sender, EventArgs e) =>
        Session.Dispatcher.Post(() => _ = ReloadAsync(userInitiated: false));

    private string Describe(PackageQueryOutcome outcome)
    {
        if (outcome.Errors.Count > 0 && outcome.Packages.Count == 0)
        {
            return string.Join(" ", outcome.Errors);
        }

        return outcome.Packages.Count == 0
            ? "Nothing to show."
            : $"{outcome.Packages.Count} package{(outcome.Packages.Count == 1 ? string.Empty : "s")}.";
    }
}
