using System.Windows;
using WingetUni.App.ViewModels;
using WingetUni.App.Views;
using WingetUni.Core.Models;

namespace WingetUni.App.Services;

/// <summary>
/// The one place that opens windows. View models ask for a dialog through
/// <see cref="IDialogService"/> so that they never hold a reference to a window.
/// </summary>
/// <remarks>
/// Dialogs are owned by the main window, which keeps them in front of it and centred on it. The
/// owner is read from the application each time rather than captured, so a dialog opened after
/// the window was recreated still belongs to the current window.
/// </remarks>
public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.MainWindow;

    public void ShowMessage(string title, string message) =>
        System.Windows.MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message) =>
        System.Windows.MessageBox.Show(Owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question)
            == MessageBoxResult.OK;

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public InstallOptions? ShowInstallOptions(
        PackageOperation operation,
        string managerDisplayName,
        Package package,
        InstallOptions defaults)
    {
        var window = new InstallOptionsWindow(
            new InstallOptionsViewModel(package, operation, defaults, managerDisplayName));

        if (Owner is { } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? window.Options : null;
    }

    public void ShowDetails(PackageDetailsViewModel details)
    {
        var window = new DetailsWindow(details);

        if (Owner is { } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
    }
}
