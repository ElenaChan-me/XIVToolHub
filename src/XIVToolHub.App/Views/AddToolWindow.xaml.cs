using System.Windows;
using XIVToolHub.App.ViewModels;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.Views;

public partial class AddToolWindow : Window
{
    private readonly AddToolViewModel _viewModel;

    public AddToolWindow(AddToolViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public ToolDefinition? Result { get; private set; }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.Build();
        if (Result is not null)
        {
            DialogResult = true;
        }
    }
}
