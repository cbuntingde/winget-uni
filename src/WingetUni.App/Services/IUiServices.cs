using WingetUni.App.ViewModels;
using WingetUni.Core.Models;

namespace WingetUni.App.Services;

/// <summary>
/// Marshals work onto the interface thread. Package managers and the operation queue run on
/// worker threads, so anything that touches the visual tree has to come back through here.
/// </summary>
public interface IUiDispatcher
{
    bool IsOnUiThread { get; }

    /// <summary>Queues <paramref name="action"/> to run on the interface thread.</summary>
    void Post(Action action);

    /// <summary>Runs <paramref name="action"/> on the interface thread and waits for it.</summary>
    Task InvokeAsync(Action action);
}

/// <summary>Collects user input that only a window can ask for.</summary>
public interface IDialogService
{
    void ShowMessage(string title, string message);

    bool Confirm(string title, string message);

    /// <summary>Asks for a file to open, or null when the user cancels.</summary>
    string? PickOpenFile(string title, string filter);

    /// <summary>Asks for a file to save to, or null when the user cancels.</summary>
    string? PickSaveFile(string title, string filter, string defaultFileName);

    /// <summary>
    /// Shows the install options dialog, returning the chosen options, or null when the user
    /// cancels. The options start from <paramref name="defaults"/>, the saved preferences.
    /// </summary>
    InstallOptions? ShowInstallOptions(
        PackageOperation operation,
        string managerDisplayName,
        Package package,
        InstallOptions defaults);

    void ShowDetails(PackageDetailsViewModel details);
}
