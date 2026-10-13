using System.Windows;
using XIVToolHub.Core;
using XIVToolHub.Core.GitHub;

namespace XIVToolHub.App.Views;

public partial class TokenWindow : Window
{
    private readonly ITokenStore _store;

    public TokenWindow(ITokenStore store)
    {
        InitializeComponent();
        _store = store;
        UpdateStatus();
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.Set(TokenBox.Password);
            TokenBox.Clear();
            MessageText.Text = "";
            UpdateStatus();
        }
        catch (HubException ex)
        {
            MessageText.Text = ex.Message;
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _store.Clear();
            MessageText.Text = "";
            UpdateStatus();
        }
        catch (HubException ex)
        {
            MessageText.Text = ex.Message;
        }
    }

    private void UpdateStatus()
    {
        try
        {
            StatusText.Text = _store.Get() is null ? "トークンは登録されていません。" : "トークンが登録されています。";
        }
        catch (HubException ex)
        {
            StatusText.Text = ex.Message;
        }
    }
}
