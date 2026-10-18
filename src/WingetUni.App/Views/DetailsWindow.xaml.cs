using System.Diagnostics;
using System.Windows;
using WingetUni.App.ViewModels;

namespace WingetUni.App.Views;

/// <summary>Shows a package's full metadata.</summary>
public partial class DetailsWindow : Window
{
    public DetailsWindow(PackageDetailsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOpenHomepage(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PackageDetailsViewModel { Homepage: { Length: > 0 } url })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                this,
                exception.Message,
                "Could not open the page",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
