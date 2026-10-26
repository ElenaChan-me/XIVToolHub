using System.Windows;
using System.Windows.Threading;
using XIVToolHub.App.Infrastructure;
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
using XIVToolHub.Core.SelfUpdate;
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
        // 利用者が別に入れている ACT が起動していても、ハブの ACT の更新は止めない
        var hubAct = new ProcessInFolderDetector(paths.ToolDirectory(ToolCatalog.ActToolId), "Advanced Combat Tracker", "ACTx86");
        var act = new ActConfigEditor(paths.ToolDirectory(ToolCatalog.ActToolId), hubAct, clock);
        // フォントの配布元や Dalamud の配布サーバーは GitHub ではないため、GitHub 用の設定を持たない別のクライアントを使う
        _fontHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _fontHttp.DefaultRequestHeaders.UserAgent.ParseAdd("XIVToolHub");
        var fonts = new FontStore(paths, _fontHttp);
        var settings = new HubSettingsStore(paths);
        ThemeManager.Apply(settings.Theme);
        var installer = new ToolInstaller(paths, github, installed, dalamud, act, gameDetector, clock, fonts.ResolvePath, () => settings.Theme,
            () => catalog.VoicePackDirectories(installed));
        var dialogs = new WpfDialogService(github, tokenStore);
        var launcher = new ToolLauncher(paths);
        var triggeventDetector = new ProcessInFolderDetector(paths.ToolDirectory(ToolCatalog.TriggeventToolId), "javaw");
        var launchSequence = new LaunchSequence(launcher.Launch, hubAct, triggeventDetector, clock, anyAct: ProcessNameDetector.Act);
        var viewModel = new MainViewModel(catalog, installed, installer, launcher, fonts, settings, ThemeManager.Apply, paths, dialogs, clock,
            new PatchStatusChecker(paths.XivLauncherConfigPath, _fontHttp, github), CreateSelfUpdater(github, paths), launchSequence, Restart,
            new UpdateBlockers(hubAct, triggeventDetector, gameDetector));

        AsyncRelayCommand.UnhandledError = ex =>
            MessageBox.Show($"処理中に予期しないエラーが発生しました。もう一度お試しください。{Environment.NewLine}{ex.Message}", "XIVToolHub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        _ = viewModel.StartupAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _http?.Dispose();
        _fontHttp?.Dispose();
        base.OnExit(e);
    }

    private static SelfUpdater? CreateSelfUpdater(GitHubReleaseClient github, HubPaths paths)
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !SelfUpdater.CanUpdate(exe))
        {
            return null;
        }
        SelfUpdater.CleanupOld(exe);
        var version = typeof(App).Assembly.GetName().Version ?? new Version(0, 0);
        return new SelfUpdater(github, paths, exe, SelfUpdater.Normalize(version));
    }

    private void Restart(string exe)
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            MessageBox.Show($"更新しましたが、起動し直せませんでした。XIVToolHub をもう一度起動してください。{Environment.NewLine}{ex.Message}", "XIVToolHub",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        Shutdown(0);
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
