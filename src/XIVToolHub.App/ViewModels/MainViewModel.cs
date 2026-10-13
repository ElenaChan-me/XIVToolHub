using System.Collections.ObjectModel;
using System.Windows.Input;
using XIVToolHub.App.Infrastructure;
using XIVToolHub.App.Services;
using XIVToolHub.Core;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 300;

    private readonly ToolCatalog _catalog;
    private readonly InstalledStore _installed;
    private readonly ToolInstaller _installer;
    private readonly ToolLauncher _launcher;
    private readonly HubPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _clock;
    private bool _isBusy;
    private ToolItemViewModel? _selectedItem;

    public MainViewModel(ToolCatalog catalog, InstalledStore installed, ToolInstaller installer, ToolLauncher launcher, HubPaths paths, IDialogService dialogs, TimeProvider clock)
    {
        _catalog = catalog;
        _installed = installed;
        _installer = installer;
        _launcher = launcher;
        _paths = paths;
        _dialogs = dialogs;
        _clock = clock;

        RefreshCommand = new AsyncRelayCommand(CheckUpdatesAsync, () => !_isBusy);
        InstallSelectedCommand = new AsyncRelayCommand(InstallSelectedAsync, () => !_isBusy && Tools.Any(t => t.IsSelected));
        UninstallSelectedCommand = new RelayCommand(UninstallSelected, () => !_isBusy && Tools.Any(t => t.IsSelected && t.IsInstalled));
        SelectAllCommand = new RelayCommand(() => SetSelection(true), () => !_isBusy);
        ClearSelectionCommand = new RelayCommand(() => SetSelection(false), () => !_isBusy);
        LaunchCommand = new RelayCommand<ToolItemViewModel>(Launch, t => !_isBusy && t.IsApplication && t.IsInstalled);
        OpenFolderCommand = new RelayCommand<ToolItemViewModel>(t => _dialogs.OpenFolder(_paths.ToolDirectory(t.Id)), t => t.IsInstalled);
        OpenRepositoryCommand = new RelayCommand<ToolItemViewModel>(t => _dialogs.OpenUrl(t.Definition.Source.RepositoryUrl));
        AddToolCommand = new RelayCommand(AddTool, () => !_isBusy);
        RemoveToolCommand = new RelayCommand<ToolItemViewModel>(RemoveTool, t => !_isBusy && !t.IsBuiltIn);
        TokenSettingsCommand = new RelayCommand(() => _dialogs.ShowTokenSettings(), () => !_isBusy);

        ReloadTools();
    }

    public ObservableCollection<ToolItemViewModel> Tools { get; } = [];

    public ObservableCollection<string> Log { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand InstallSelectedCommand { get; }
    public ICommand UninstallSelectedCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenRepositoryCommand { get; }
    public ICommand AddToolCommand { get; }
    public ICommand RemoveToolCommand { get; }
    public ICommand TokenSettingsCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ToolItemViewModel? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    public string DataDirectory => _paths.DataDirectory;

    public async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            AddLog("最新バージョンを確認しています。");
            // 同時に問い合わせて待ち時間を短くする。件数は利用者のカタログ程度なので制限は設けない。
            await Task.WhenAll(Tools.Select(CheckOneAsync));
            AddLog("確認が完了しました。");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckOneAsync(ToolItemViewModel item)
    {
        try
        {
            var release = await _installer.GetLatestAsync(item.Definition, CancellationToken.None);
            item.LatestVersion = release.TagName;
            item.Error = null;
        }
        catch (HubException ex)
        {
            item.Error = ex.Message;
            AddLog($"{item.Name}: {ex.Message}");
        }
    }

    private async Task InstallSelectedAsync()
    {
        var targets = Tools.Where(t => t.IsSelected).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        IsBusy = true;
        var succeeded = 0;
        var devModeEnabled = false;
        try
        {
            // 1件ずつ処理し、失敗しても残りは続ける
            foreach (var item in targets)
            {
                item.IsWorking = true;
                item.Error = null;
                var progress = new Progress<InstallProgress>(p =>
                {
                    item.ProgressText = p.Stage;
                    item.Progress = p.Fraction ?? item.Progress;
                });
                try
                {
                    var result = await _installer.InstallAsync(item.Definition, force: false, progress, CancellationToken.None);
                    item.InstalledVersion = result.Version;
                    item.LatestVersion = result.Version;
                    devModeEnabled |= result.DevModeEnabled;
                    succeeded++;
                    AddLog(result.AlreadyLatest
                        ? $"{item.Name}: 最新版（{result.Version}）がインストール済みです。"
                        : $"{item.Name}: {result.Version} をインストールしました。");
                }
                catch (HubException ex)
                {
                    item.Error = ex.Message;
                    AddLog($"{item.Name}: {ex.Message}");
                }
                finally
                {
                    item.IsWorking = false;
                    item.ProgressText = "";
                    item.Progress = 0;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        var message = $"{targets.Count} 件中 {succeeded} 件を処理しました。";
        if (devModeEnabled)
        {
            message += "\n\nDalamud の開発モードを有効にしました。次回ゲーム起動時から、インストールしたプラグインが読み込まれます。";
        }
        if (succeeded == targets.Count)
        {
            _dialogs.ShowInfo(message);
        }
        else
        {
            _dialogs.ShowError(message + "\n\n失敗した理由はログを確認してください。");
        }
    }

    private void UninstallSelected()
    {
        var targets = Tools.Where(t => t.IsSelected && t.IsInstalled).ToList();
        if (targets.Count == 0
            || !_dialogs.Confirm($"次のツールをアンインストールしますか？\n\n{string.Join("\n", targets.Select(t => "・" + t.Name))}", "アンインストールの確認"))
        {
            return;
        }

        foreach (var item in targets)
        {
            try
            {
                _installer.Uninstall(item.Definition);
                item.InstalledVersion = null;
                AddLog($"{item.Name}: アンインストールしました。");
            }
            catch (HubException ex)
            {
                item.Error = ex.Message;
                AddLog($"{item.Name}: {ex.Message}");
                _dialogs.ShowError($"{item.Name} をアンインストールできませんでした。\n{ex.Message}");
            }
        }
    }

    private void Launch(ToolItemViewModel item)
    {
        try
        {
            _launcher.Launch(item.Definition);
            AddLog($"{item.Name} を起動しました。");
        }
        catch (HubException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private void AddTool()
    {
        var definition = _dialogs.ShowAddTool();
        if (definition is null)
        {
            return;
        }
        try
        {
            _catalog.AddUserTool(definition);
            ReloadTools();
            AddLog($"{definition.Name} をカタログに追加しました。");
        }
        catch (HubException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private void RemoveTool(ToolItemViewModel item)
    {
        var message = item.IsInstalled
            ? $"{item.Name} をカタログから削除しますか？\nインストール済みのファイルもアンインストールします。"
            : $"{item.Name} をカタログから削除しますか？";
        if (!_dialogs.Confirm(message, "カタログから削除"))
        {
            return;
        }
        try
        {
            if (item.IsInstalled)
            {
                _installer.Uninstall(item.Definition);
            }
            _catalog.RemoveUserTool(item.Id);
            ReloadTools();
            AddLog($"{item.Name} をカタログから削除しました。");
        }
        catch (HubException ex)
        {
            _dialogs.ShowError(ex.Message);
        }
    }

    private void SetSelection(bool selected)
    {
        foreach (var item in Tools)
        {
            item.IsSelected = selected;
        }
    }

    private void ReloadTools()
    {
        var previous = Tools.ToDictionary(t => t.Id);
        Tools.Clear();
        foreach (var definition in _catalog.Tools)
        {
            var item = new ToolItemViewModel(definition, _installed.Find(definition.Id));
            if (previous.TryGetValue(definition.Id, out var old))
            {
                item.LatestVersion = old.LatestVersion;
                item.IsSelected = old.IsSelected;
            }
            Tools.Add(item);
        }
    }

    private void AddLog(string message)
    {
        Log.Insert(0, $"{_clock.GetLocalNow():HH:mm:ss}  {message}");
        while (Log.Count > MaxLogLines)
        {
            Log.RemoveAt(Log.Count - 1);
        }
    }
}
