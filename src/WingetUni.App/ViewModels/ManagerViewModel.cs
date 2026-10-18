using WingetUni.App.Infrastructure;
using WingetUni.Core.Abstractions;

namespace WingetUni.App.ViewModels;

/// <summary>
/// One package manager, shown in settings and used as a filter while searching.
/// </summary>
/// <remarks>
/// A manager that is not installed is still listed. Its probe recorded why it is unavailable,
/// which is more useful to a user than the manager silently vanishing from the screen.
/// </remarks>
public sealed class ManagerViewModel : ObservableObject
{
    private bool _isEnabled = true;

    public ManagerViewModel(IPackageManagerProvider provider)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public IPackageManagerProvider Provider { get; }

    public string Id => Provider.Id;

    public string DisplayName => Provider.DisplayName;

    public bool IsAvailable => Provider.IsAvailable;

    public string? Version => Provider.Version;

    public string? UnavailableReason => Provider.UnavailableReason;

    public string StatusText => IsAvailable
        ? $"Version {Version ?? "unknown"}"
        : UnavailableReason ?? "Not installed";

    /// <summary>Whether the user has left this manager switched on in settings.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>Re-reads the probe result after <see cref="AppSession.ProbeAsync"/> has run.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(Version));
        OnPropertyChanged(nameof(UnavailableReason));
        OnPropertyChanged(nameof(StatusText));
    }
}
