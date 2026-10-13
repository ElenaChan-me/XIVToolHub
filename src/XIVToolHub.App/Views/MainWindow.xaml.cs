using System.Windows;
using XIVToolHub.App.ViewModels;

namespace XIVToolHub.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
