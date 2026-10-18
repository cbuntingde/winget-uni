using System.Collections.ObjectModel;
using WingetUni.App.Infrastructure;
using WingetUni.Core.Models;

namespace WingetUni.App.ViewModels;

/// <summary>One package as a row in a list.</summary>
public sealed class PackageViewModel : ObservableObject
{
    private bool _isSelected;

    public PackageViewModel(Package model, string managerDisplayName)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        ManagerName = managerDisplayName;
    }

    public Package Model { get; }

    /// <summary>The buttons this row offers, decided by the page that built it.</summary>
    public ObservableCollection<PackageActionViewModel> Actions { get; } = [];

    public string ManagerId => Model.ManagerId;

    public string ManagerName { get; }

    public string Id => Model.Id;

    public string Name => Model.Name;

    public string? Source => Model.Source;

    public string? Match => Model.Match;

    public bool IsInstalled => Model.IsInstalled;

    public bool HasUpdate => Model.HasUpdate;

    public string? InstalledVersion => Model.InstalledVersion;

    public string? AvailableVersion => Model.AvailableVersion;

    public string? UpdateVersion => Model.UpdateVersion;

    /// <summary>The version worth showing in the list, whichever kind the listing carried.</summary>
    public string VersionText => Model.InstalledVersion ?? Model.AvailableVersion ?? "—";

    /// <summary>Describes what an update would change, empty when there is no update.</summary>
    public string UpdateText => Model.HasUpdate ? $"{Model.InstalledVersion}  →  {Model.UpdateVersion}" : string.Empty;

    /// <summary>The identifier, manager, and source as one line under the package name.</summary>
    public string Subtitle => Source is { Length: > 0 } source
        ? $"{Id}  ·  {ManagerName}  ·  {source}"
        : $"{Id}  ·  {ManagerName}";

    /// <summary>Ticked in the updates list to choose which packages a bulk update acts on.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
