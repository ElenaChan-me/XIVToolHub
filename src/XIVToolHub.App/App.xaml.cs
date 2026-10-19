using System.Windows;
using System.Windows.Threading;
using XIVToolHub.App.Services;
using XIVToolHub.App.ViewModels;
using XIVToolHub.App.Views;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Compatibility;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Fonts;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.App;

public partial class App : Application
{
    private HttpClient? _http;
    private HttpClient? _fontHttp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var paths = HubPaths.Default;
        var clock = TimeProvider.System;
        var tokenStore = new CredentialTokenStore();
        _http = GitHubReleaseClient.CreateHttpClient();
        var github = new GitHubReleaseClient(_http, () => ReadToken(tokenStore));
        var gameDetector = ProcessNameDetector.Game;

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
        var act = new ActConfigEditor(paths.ToolDirectory(ToolCatalog.ActToolId), ProcessNameDetector.Act, clock);
        // フォントの配布元や Dalamud の配布サーバーは GitHub ではないため、GitHub 用の設定を持たない別のクライアントを使う
        _fontHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _fontHttp.DefaultRequestHeaders.UserAgent.ParseAdd("XIVToolHub");
        var fonts = new FontStore(paths, _fontHttp);
        var settings = new HubSettingsStore(paths);
        ThemeManager.Apply(settings.Theme);
        var installer = new ToolInstaller(paths, github, installed, dalamud, act, gameDetector, clock, fonts.ResolvePath, () => settings.Theme);
        var dialogs = new WpfDialogService(github, tokenStore);
        var viewModel = new MainViewModel(catalog, installed, installer, new ToolLauncher(paths), fonts, settings, ThemeManager.Apply, paths, dialogs, clock,
            new PatchStatusChecker(paths.XivLauncherConfigPath, _fontHttp, github));

        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        _ = viewModel.CheckUpdatesAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _http?.Dispose();
        _fontHttp?.Dispose();
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
