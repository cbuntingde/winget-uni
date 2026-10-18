using System.ComponentModel;
using System.Windows;
using WingetUni.App.ViewModels;

namespace WingetUni.App;

/// <summary>The window that hosts the rail, the pages, and the operation panel.</summary>
/// <remarks>
/// What the X button does is the user's choice, held in
/// <see cref="Core.Services.AppSettings.MinimizeToTrayOnClose"/>. When it is set, closing or
/// minimising hides the window in the notification area rather than ending the application,
/// because a long install should survive the window being put away. The application sets
/// <see cref="AllowClose"/> when it really does intend to exit.
/// </remarks>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += OnStateChanged;
    }

    /// <summary>Raised when the user closes the window and the application should end.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>True once the application means to close the window rather than hide it.</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            if (MinimizeOnClose)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                // The application keeps running without a window, so closing this one is not
                // enough: it has to be told to end.
                AllowClose = true;
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        base.OnClosing(e);
    }

    private bool MinimizeOnClose =>
        (DataContext as MainViewModel)?.Session.Options.MinimizeToTrayOnClose ?? true;

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }
}
