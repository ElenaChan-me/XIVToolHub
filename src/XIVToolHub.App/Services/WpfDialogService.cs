using System.Diagnostics;
using System.Windows;
using XIVToolHub.App.ViewModels;
using XIVToolHub.App.Views;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.Services;

public sealed class WpfDialogService(GitHubReleaseClient github, ITokenStore tokenStore) : IDialogService
{
    private const string Title = "XIVToolHub";

    private static Window? Owner =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public void ShowInfo(string message) => Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowError(string message) => Show(message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public bool Confirm(string message, string title = "確認") =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public ToolDefinition? ShowAddTool()
    {
        var window = new AddToolWindow(new AddToolViewModel(github)) { Owner = Owner };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public void ShowTokenSettings()
    {
        var window = new TokenWindow(tokenStore) { Owner = Owner };
        window.ShowDialog();
    }

    public string? ShowLanguageSelection(IReadOnlyList<ToolDefinition> tools)
    {
        var window = new LanguageWindow(tools) { Owner = Owner };
        return window.ShowDialog() == true ? window.Result : null;
    }

    public bool ShowToolSettings(ToolSettingsViewModel viewModel)
    {
        var window = new ToolSettingsWindow(viewModel) { Owner = Owner };
        return window.ShowDialog() == true;
    }

    public string? PickFontFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "フォントファイルを選択",
            Filter = "フォント・ZIP (*.ttf;*.otf;*.ttc;*.zip)|*.ttf;*.otf;*.ttc;*.zip",
            CheckFileExists = true,
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void OpenWebPage(string url)
    {
        // 配布元に HTTP だけのサイトもあるため HTTP も許可するが、それ以外のスキームは開かない
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            ShowError("開けない URL です。");
            return;
        }
        Start("explorer.exe", uri.AbsoluteUri);
    }

    public void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            ShowError("フォルダがまだありません。");
            return;
        }
        Start("explorer.exe", path);
    }

    public void OpenUrl(string url)
    {
        // カタログで検証済みの GitHub の URL だけを開く
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "github.com")
        {
            ShowError("開けない URL です。");
            return;
        }
        Start("explorer.exe", uri.AbsoluteUri);
    }

    private void Start(string fileName, string argument)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(fileName) { ArgumentList = { argument }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowError($"開けませんでした。\n{ex.Message}");
        }
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = Owner;
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
    }
}
