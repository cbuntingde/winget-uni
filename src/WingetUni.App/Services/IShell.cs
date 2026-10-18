using WingetUni.App.ViewModels;
using WingetUni.Core.Models;

namespace WingetUni.App.Services;

/// <summary>
/// The part of the shell a page is allowed to use: navigation, the busy indicator, and the two
/// actions that need the window, queueing an operation and showing package details.
/// </summary>
/// <remarks>
/// Pages depend on this rather than on the shell view model itself, so a page cannot reach into
/// the shell's other pages and the dependency stays one-way.
/// </remarks>
public interface IShell
{
    AppSession Session { get; }

    void Navigate(string section);

    /// <summary>Applies a theme and remembers it as the user's choice.</summary>
    void ApplyTheme(string theme);

    /// <summary>Shows or clears the progress indicator and sets the status text.</summary>
    void SetBusy(bool busy, string? message = null);

    /// <summary>Sets the status text without changing the busy state.</summary>
    void Report(string message);

    /// <summary>Queues one install, update, or removal.</summary>
    void Enqueue(PackageOperation operation, PackageViewModel package, InstallOptions options);

    /// <summary>Reads and shows a package's full metadata.</summary>
    Task ShowDetailsAsync(PackageViewModel package);
}
