using System.Windows.Input;

namespace WingetUni.App.ViewModels;

/// <summary>
/// One button on a package row: the label to show and the page command it runs for that package.
/// </summary>
/// <remarks>
/// The row template is shared by every page, so the buttons a page offers are data attached to
/// the package rather than markup. Each page decides which actions its rows carry, and the one
/// template renders whatever it is given.
/// </remarks>
public sealed class PackageActionViewModel
{
    public PackageActionViewModel(
        string label,
        ICommand command,
        object? parameter,
        bool isPrimary = false,
        bool isDanger = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Label = label;
        Command = command ?? throw new ArgumentNullException(nameof(command));
        Parameter = parameter;
        IsPrimary = isPrimary;
        IsDanger = isDanger;
    }

    public string Label { get; }

    public ICommand Command { get; }

    /// <summary>What the command acts on, which for a row action is always the package.</summary>
    public object? Parameter { get; }

    /// <summary>Accent styling for the action the page expects most often.</summary>
    public bool IsPrimary { get; }

    /// <summary>Destructive styling, used for removal.</summary>
    public bool IsDanger { get; }
}
