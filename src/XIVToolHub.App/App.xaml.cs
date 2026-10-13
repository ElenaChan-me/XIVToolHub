using System.Windows;
using System.Windows.Threading;
using XIVToolHub.App.Services;
using XIVToolHub.App.ViewModels;
using XIVToolHub.App.Views;
using XIVToolHub.Core;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.App;

public partial class App : Application
{
    private HttpClient? _http;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var paths = HubPaths.Default;
        var clock = TimeProvider.System;
        var tokenStore = new CredentialTokenStore();
        _http = GitHubReleaseClient.CreateHttpClient();
        var github = new GitHubReleaseClient(_http, () => ReadToken(tokenStore));
        var gameDetector = new GameProcessDetector();

        ToolCatalog catalog;
        InstalledStore installed;
        try
        {
            catalog = new ToolCatalog(paths);
            installed = new InstalledStore(paths);
        }
        catch (HubException ex)
        {
            MessageBox.Show(ex.Message, "XIVToolHub", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        var dalamud = new DalamudConfigEditor(paths.DalamudConfigPath, gameDetector, clock);
        var installer = new ToolInstaller(paths, github, installed, dalamud, gameDetector, clock);
        var dialogs = new WpfDialogService(github, tokenStore);
        var viewModel = new MainViewModel(catalog, installed, installer, new ToolLauncher(paths), paths, dialogs, clock);

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        _ = viewModel.CheckUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _http?.Dispose();
        base.OnExit(e);
    }

    private static string? ReadToken(CredentialTokenStore store)
    {
        try
        {
            return store.Get();
        }
        catch (HubException)
        {
            // 読めない場合はトークンなしで続け、Public リポジトリは取得できるようにする
            return null;
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"予期しないエラーが発生しました。アプリを終了します。\n\n{e.Exception.Message}", "XIVToolHub",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Shutdown(1);
    }
}
