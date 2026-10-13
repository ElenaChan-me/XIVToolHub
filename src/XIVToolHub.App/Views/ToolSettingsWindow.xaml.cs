using System.Windows;
using XIVToolHub.App.ViewModels;

namespace XIVToolHub.App.Views;

public partial class ToolSettingsWindow : Window
{
    private readonly ToolSettingsViewModel _viewModel;

    public ToolSettingsWindow(ToolSettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Save())
        {
            DialogResult = true;
        }
    }
}
