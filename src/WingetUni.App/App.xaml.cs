using System.Windows;
using System.Windows.Threading;
using WingetUni.App.Infrastructure;
using WingetUni.App.Services;
using WingetUni.App.ViewModels;
using WingetUni.Core.Processes;
using WingetUni.Core.Services;

namespace WingetUni.App;

/// <summary>
/// Builds the object graph, shows the window, and keeps the application alive in the tray.
/// </summary>
/// <remarks>
/// The window hides instead of closing, so the application decides when it ends. That is why the
/// shutdown mode is explicit: without it the hidden window would end the process.
/// </remarks>
public partial class App : Application
{
    private AppSession? _session;
    private MainViewModel? _viewModel;
    private TrayIconService? _tray;
    private MainWindow? _window;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _session = new AppSession(
            new ProcessRunner(),
            new DialogService(),
            new WpfDispatcher(Dispatcher),
            new SettingsService(),
            new PackageListService());

        _viewModel = new MainViewModel(_session);
        _viewModel.ExitRequested += (_, _) => ExitApplication();
        _viewModel.UpdatesChecked += OnUpdatesChecked;

        _window = new MainWindow { DataContext = _viewModel };
        _window.ExitRequested += (_, _) => ExitApplication();
        _window.Show();

        _tray = new TrayIconService("Winget Uni", ShowWindow, ExitApplication);

        await _viewModel.InitializeAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _viewModel?.Dispose();
        _session?.Dispose();

        base.OnExit(e);
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void ExitApplication()
    {
        if (_window is not null)
        {
            _window.AllowClose = true;
        }

        Shutdown();
    }

    private void OnUpdatesChecked(object? sender, int count)
    {
        if (count > 0 && _viewModel?.Session.Options.NotifyWhenUpdatesAreAvailable == true)
        {
            _tray?.ShowUpdateNotice(count);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ReportUnexpected(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ReportUnexpected(e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            return;
        }

        // A failure on a background thread cannot be recovered from; report it before the process ends.
        System.Windows.MessageBox.Show(
            exception.ToString(),
            "Unexpected error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void ReportUnexpected(Exception exception)
    {
        try
        {
            _viewModel?.Report($"Unexpected error: {exception.Message}");
            _session?.Dialogs.ShowMessage("Unexpected error", exception.Message);
        }
        catch
        {
            // Reporting a failure must never itself become the failure.
        }
    }
}
