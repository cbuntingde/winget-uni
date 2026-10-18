using WingetUni.App.Infrastructure;
using WingetUni.Core.Models;

namespace WingetUni.App.ViewModels;

/// <summary>Options for a single install, update, or removal, edited in a dialog.</summary>
public sealed class InstallOptionsViewModel : ObservableObject
{
    private string? _version;
    private string _scope = string.Empty;
    private string? _location;
    private string _customArguments = string.Empty;
    private bool _silent;
    private bool _acceptAgreements;

    public InstallOptionsViewModel(Package package, PackageOperation operation, InstallOptions defaults, string managerName)
    {
        ArgumentNullException.ThrowIfNull(package);

        Package = package;
        Operation = operation;
        ManagerName = managerName;

        _version = defaults.Version;
        _scope = defaults.Scope ?? string.Empty;
        _location = defaults.Location;
        _customArguments = defaults.CustomArguments ?? string.Empty;
        _silent = defaults.Silent;
        _acceptAgreements = defaults.AcceptAgreements;
    }

    public Package Package { get; }

    public PackageOperation Operation { get; }

    public string ManagerName { get; }

    public string WindowTitle => Operation switch
    {
        PackageOperation.Install => "Install options",
        PackageOperation.Update => "Update options",
        PackageOperation.Uninstall => "Removal options",
        _ => "Options",
    };

    /// <summary>Text of the dialog's confirming button, matching the operation.</summary>
    public string ConfirmLabel => Operation switch
    {
        PackageOperation.Install => "Install",
        PackageOperation.Update => "Update",
        PackageOperation.Uninstall => "Remove",
        _ => "Continue",
    };

    public string PackageName => Package.Name;

    public string PackageId => Package.Id;

    public string VersionLabel => Operation == PackageOperation.Uninstall ? "Not used when removing" : "Version";

    /// <summary>Empty means "whatever the manager would pick", which is the newest release.</summary>
    public string? Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    /// <summary>Empty, "user", or "machine".</summary>
    public string Scope
    {
        get => _scope;
        set
        {
            if (SetProperty(ref _scope, value))
            {
                OnPropertyChanged(nameof(IsDefaultScope));
                OnPropertyChanged(nameof(IsUserScope));
                OnPropertyChanged(nameof(IsMachineScope));
            }
        }
    }

    public bool IsDefaultScope
    {
        get => Scope.Length == 0;
        set { if (value) Scope = string.Empty; }
    }

    public bool IsUserScope
    {
        get => string.Equals(Scope, "user", StringComparison.OrdinalIgnoreCase);
        set { if (value) Scope = "user"; }
    }

    public bool IsMachineScope
    {
        get => string.Equals(Scope, "machine", StringComparison.OrdinalIgnoreCase);
        set { if (value) Scope = "machine"; }
    }

    public string? Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public string CustomArguments
    {
        get => _customArguments;
        set => SetProperty(ref _customArguments, value);
    }

    public bool Silent
    {
        get => _silent;
        set => SetProperty(ref _silent, value);
    }

    public bool AcceptAgreements
    {
        get => _acceptAgreements;
        set => SetProperty(ref _acceptAgreements, value);
    }

    public InstallOptions ToOptions() => new()
    {
        Version = string.IsNullOrWhiteSpace(_version) ? null : _version!.Trim(),
        Scope = string.IsNullOrWhiteSpace(_scope) ? null : _scope,
        Location = string.IsNullOrWhiteSpace(_location) ? null : _location!.Trim(),
        CustomArguments = string.IsNullOrWhiteSpace(_customArguments) ? null : _customArguments,
        Silent = _silent,
        AcceptAgreements = _acceptAgreements,
    };
}
