using System.Windows;
using WingetUni.App.ViewModels;
using WingetUni.Core.Models;

namespace WingetUni.App.Views;

/// <summary>Collects the options for one install, update, or removal.</summary>
public partial class InstallOptionsWindow : Window
{
    public InstallOptionsWindow(InstallOptionsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>The options the user chose, once the dialog has been accepted.</summary>
    public InstallOptions Options => ((InstallOptionsViewModel)DataContext).ToOptions();

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;
}
