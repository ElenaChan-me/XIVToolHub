using System.Collections.ObjectModel;
using System.Windows.Input;
using XIVToolHub.App.Infrastructure;
using XIVToolHub.App.Services;
using XIVToolHub.Core;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Compatibility;
using XIVToolHub.Core.Fonts;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Prerequisites;
using XIVToolHub.Core.SelfUpdate;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxLogLines = 300;

    private readonly ToolCatalog _catalog;
    private readonly InstalledStore _installed;
    private readonly ToolInstaller _installer;
    private readonly ToolLauncher _launcher;
    private readonly FontStore _fonts;
    private readonly HubSettingsStore _settings;
    private readonly Action<string> _applyTheme;
    private readonly HubPaths _paths;
    private readonly IDialogService _dialogs;
    private readonly TimeProvider _clock;
    private readonly PatchStatusChecker? _patchStatus;
    private readonly SelfUpdater? _selfUpdater;
    private readonly LaunchSequence? _launchSequence;
    private readonly Action<string> _restart;
    private readonly UpdateBlockers? _blockers;
    private readonly PrerequisiteManager? _prerequisites;
    // 画面のスレッド。チェックボックスの変更の最中に確認の画面を出さないよう、終わってから聞くために使う
    private readonly SynchronizationContext? _ui;
    // 「すべて選択」の最中は、ツールごとに確認しない
    private bool _bulkSelecting;
    private CancellationTokenSource? _updateAllWait;
    private string? _updateAllWaitText;
    private ReleaseInfo? _selfUpdate;
    private string? _patchStatusText;
    private bool _patchNeedsAttention;
    private bool _isBusy;
    // インストール・更新中のツール。ほかのツールの起動などは続けて使えるようにする
    private readonly HashSet<string> _installing = [];
    private double _overallProgress;
    private string _overallProgressText = "";
    private ToolItemViewModel? _selectedItem;

    /// <param name="applyTheme">画面の配色を切り替える処理。</param>
    public MainViewModel(ToolCatalog catalog, InstalledStore installed, ToolInstaller installer, ToolLauncher launcher, FontStore fonts, HubSettingsStore settings,
        Action<string> applyTheme, HubPaths paths, IDialogService dialogs, TimeProvider clock, PatchStatusChecker? patchStatus = null,
        SelfUpdater? selfUpdater = null, LaunchSequence? launchSequence = null, Action<string>? restart = null, UpdateBlockers? blockers = null,
        PrerequisiteManager? prerequisites = null)
    {
        _blockers = blockers;
        _prerequisites = prerequisites;
        _ui = SynchronizationContext.Current as System.Windows.Threading.DispatcherSynchronizationContext;
        _patchStatus = patchStatus;
        _selfUpdater = selfUpdater;
        _launchSequence = launchSequence;
        _restart = restart ?? (_ => { });
        _fonts = fonts;
        _settings = settings;
        _applyTheme = applyTheme;
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
        // 選択はインストール中でも変えてよい（処理するツールは押した時点で決まっている）
        SelectAllCommand = new RelayCommand(() => SetSelection(true));
        ClearSelectionCommand = new RelayCommand(() => SetSelection(false));
        // インストール中でも、入れ替えていないツールは起動できる
        LaunchCommand = new RelayCommand<ToolItemViewModel>(Launch, t => t.IsApplication && t.IsInstalled && !t.IsWorking && !_installing.Contains(t.Id));
        OpenFolderCommand = new RelayCommand<ToolItemViewModel>(t => _dialogs.OpenFolder(_paths.ToolDirectory(t.Id)), t => t.IsInstalled);
        OpenRepositoryCommand = new RelayCommand<ToolItemViewModel>(t => _dialogs.OpenUrl(t.Definition.Source.RepositoryUrl));
        AddToolCommand = new RelayCommand(AddTool, () => !_isBusy);
        RemoveToolCommand = new RelayCommand<ToolItemViewModel>(RemoveTool, t => !_isBusy && !t.IsBuiltIn);
        TokenSettingsCommand = new RelayCommand(() => _dialogs.ShowTokenSettings(), () => !_isBusy);
        ToolSettingsCommand = new RelayCommand<ToolItemViewModel>(OpenToolSettings, t => !_isBusy && t.CanConfigure);
        UpdateSelfCommand = new AsyncRelayCommand(UpdateSelfAsync, () => !_isBusy && _selfUpdate is not null);
        LaunchAllCommand = new AsyncRelayCommand(LaunchAllAsync, () => !_isBusy && CanLaunchAll);
        UpdateAllCommand = new AsyncRelayCommand(UpdateAllAsync, () => !_isBusy && _updateAllWait is null && UpdateCount > 0);
        CancelUpdateAllCommand = new RelayCommand(() => _updateAllWait?.Cancel(), () => _updateAllWait is not null);

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
    public ICommand ToolSettingsCommand { get; }
    public ICommand UpdateSelfCommand { get; }
    public ICommand LaunchAllCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand CancelUpdateAllCommand { get; }

    /// <summary>更新のあるインストール済みツールの数。</summary>
    public int UpdateCount => Tools.Count(t => t.State == ToolState.UpdateAvailable);

    public bool HasUpdates => UpdateCount > 0 || _updateAllWait is not null;

    /// <summary>まとめて更新の案内。待っている間は、何が終わるのを待っているかを出す。</summary>
    public string UpdatesText => _updateAllWaitText
        ?? $"{UpdateCount} 件のツールに更新があります：{string.Join("、", Tools.Where(t => t.State == ToolState.UpdateAvailable).Select(t => t.Name))}";

    public bool IsWaitingForUpdateAll => _updateAllWait is not null;

    /// <summary>XIVToolHub の新しいバージョンがあるときの案内。無ければ null。</summary>
    public string? SelfUpdateText => _selfUpdate is null || _selfUpdater is null
        ? null
        : $"XIVToolHub の新しいバージョン {_selfUpdate.TagName} があります（今は v{_selfUpdater.CurrentVersion.ToString(3)}）。";

    public bool HasSelfUpdate => _selfUpdate is not null;

    /// <summary>ACT と Triggevent Plus がどちらもインストール済みか。</summary>
    public bool CanLaunchAll => _launchSequence is not null
        && Tools.Any(t => t.Id == ToolCatalog.ActToolId && t.IsInstalled)
        && Tools.Any(t => t.Id == ToolCatalog.TriggeventToolId && t.IsInstalled);

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

    /// <summary>インストール・更新の最中か。画面の下に進み具合を出す。</summary>
    public bool IsInstalling => _installing.Count > 0;

    /// <summary>インストール・更新の全体の進み具合（0〜1）。</summary>
    public double OverallProgress { get => _overallProgress; private set => SetProperty(ref _overallProgress, value); }

    public string OverallProgressText { get => _overallProgressText; private set => SetProperty(ref _overallProgressText, value); }

    public IReadOnlyList<ThemeOption> Themes => HubThemes.All;

    /// <summary>
    /// ハブとツールで共通のテーマ。変えるとインストール済みのツールの設定ファイルも書き直す。
    /// </summary>
    public string Theme
    {
        get => _settings.Theme;
        set
        {
            if (value == _settings.Theme || !HubThemes.IsSupported(value))
            {
                return;
            }
            try
            {
                _settings.SetTheme(value);
            }
            catch (HubException ex)
            {
                _dialogs.ShowError(ex.Message);
                return;
            }
            _applyTheme(value);
            OnPropertyChanged();
            RefreshToolSettings("テーマ");
            AddLog($"テーマを「{HubThemes.All.First(t => t.Code == value).DisplayName}」にしました。ツールには次回の起動時に反映されます。");
        }
    }

    /// <summary>インストール済みのツールの設定ファイルを今の設定で書き直す。</summary>
    private void RefreshToolSettings(string what)
    {
        foreach (var item in Tools.Where(t => t.CanConfigure))
        {
            try
            {
                _installer.RefreshSettings(item.Definition);
            }
            catch (HubException ex)
            {
                AddLog($"{item.Name}: {what}を反映できませんでした。{ex.Message}");
            }
        }
    }

    /// <summary>ボイスパックを入れ替えたら、Triggevent Plus に渡す一覧も書き直す。</summary>
    private void RefreshVoicePacks(IEnumerable<ToolItemViewModel> changed)
    {
        if (changed.Any(t => t.Definition.Kind == ToolKind.VoicePack))
        {
            RefreshToolSettings("ボイスパックの一覧");
        }
    }

    public ToolItemViewModel? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

    public string DataDirectory => _paths.DataDirectory;

    /// <summary>Dalamud と FFXIV_ACT_Plugin が今のゲームに対応しているか。確認前は null。</summary>
    public string? PatchStatusText
    {
        get => _patchStatusText;
        private set
        {
            if (SetProperty(ref _patchStatusText, value))
            {
                OnPropertyChanged(nameof(HasPatchStatus));
            }
        }
    }

    public bool HasPatchStatus => _patchStatusText is not null;

    public bool PatchNeedsAttention { get => _patchNeedsAttention; private set => SetProperty(ref _patchNeedsAttention, value); }

    public async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        try
        {
            AddLog("最新バージョンを確認しています。");
            // 同時に問い合わせて待ち時間を短くする。件数は利用者のカタログ程度なので制限は設けない。
            await Task.WhenAll([.. Tools.Select(CheckOneAsync), CheckPatchStatusAsync(), CheckSelfUpdateAsync()]);
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

    private async Task CheckSelfUpdateAsync()
    {
        if (_selfUpdater is null)
        {
            return;
        }
        try
        {
            _selfUpdate = await _selfUpdater.CheckAsync(CancellationToken.None);
        }
        catch (HubException ex)
        {
            // 確認できなくてもツールの管理は続けられるので、ログに残すだけにする
            _selfUpdate = null;
            AddLog($"XIVToolHub の更新を確認できませんでした。{ex.Message}");
        }
        OnPropertyChanged(nameof(SelfUpdateText));
        OnPropertyChanged(nameof(HasSelfUpdate));
        if (SelfUpdateText is { } text)
        {
            AddLog(text);
        }
    }

    private Task UpdateSelfAsync() => UpdateSelfAsync(confirmed: false);

    private async Task UpdateSelfAsync(bool confirmed)
    {
        if (_selfUpdater is null || _selfUpdate is not { } release
            || (!confirmed && !_dialogs.Confirm($"XIVToolHub を {release.TagName} に更新して再起動しますか？", "XIVToolHub の更新")))
        {
            return;
        }
        IsBusy = true;
        string exe;
        try
        {
            AddLog($"XIVToolHub {release.TagName} をダウンロードしています。");
            exe = await _selfUpdater.InstallAsync(release, null, CancellationToken.None);
        }
        catch (HubException ex)
        {
            AddLog(ex.Message);
            _dialogs.ShowError(ex.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }
        _restart(exe);
    }

    private async Task LaunchAllAsync()
    {
        var act = Tools.FirstOrDefault(t => t.Id == ToolCatalog.ActToolId);
        var triggevent = Tools.FirstOrDefault(t => t.Id == ToolCatalog.TriggeventToolId);
        if (_launchSequence is null || act is null || triggevent is null)
        {
            return;
        }
        IsBusy = true;
        try
        {
            AddLog("ACT と Triggevent Plus を起動しています。OverlayPlugin の準備を待つので少しかかります。");
            var messages = await _launchSequence.RunAsync(act.Definition, _paths.ToolDirectory(act.Id), triggevent.Definition,
                LaunchSequence.DefaultWaitTimeout, CancellationToken.None);
            foreach (var message in messages)
            {
                AddLog(message);
            }
        }
        catch (HubException ex)
        {
            AddLog(ex.Message);
            _dialogs.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckPatchStatusAsync()
    {
        if (_patchStatus is null)
        {
            return;
        }
        var status = await _patchStatus.CheckAsync(CancellationToken.None);
        PatchStatusText = PatchStatusChecker.Describe(status);
        PatchNeedsAttention = PatchStatusChecker.NeedsAttention(status);
        foreach (var problem in status.Problems)
        {
            AddLog(problem);
        }
        if (PatchNeedsAttention)
        {
            AddLog(PatchStatusText);
        }
    }

    /// <summary>
    /// 更新のあるツールをまとめて更新する。ACT などが起動中で入れ替えられないときは、終了するまで待ってから始める。
    /// </summary>
    private async Task UpdateAllAsync()
    {
        var targets = Tools.Where(t => t.State == ToolState.UpdateAvailable).ToList();
        if (targets.Count == 0)
        {
            return;
        }
        if (_blockers is not null)
        {
            using var wait = new CancellationTokenSource();
            var ui = SynchronizationContext.Current;
            SetUpdateAllWait(wait, null);
            try
            {
                var ready = await _blockers.WaitAsync([.. targets.Select(t => t.Definition)], blocking =>
                {
                    var names = string.Join("・", blocking);
                    void Show()
                    {
                        if (_updateAllWait != wait)
                        {
                            return;
                        }
                        SetUpdateAllWait(wait, $"{names} を終了すると、{targets.Count} 件の更新を始めます。");
                        AddLog($"{names} の起動中は更新できないため、終了するのを待っています。");
                    }
                    if (ui is null)
                    {
                        Show();
                    }
                    else
                    {
                        ui.Post(_ => Show(), null);
                    }
                }, UpdateBlockers.DefaultPollInterval, UpdateBlockers.DefaultTimeout, _clock, wait.Token);
                if (!ready)
                {
                    AddLog("待ち時間が長くなったため、まとめて更新をやめました。");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                AddLog("まとめて更新をやめました。");
                return;
            }
            finally
            {
                SetUpdateAllWait(null, null);
            }
        }
        await InstallToolsAsync(targets.Select(t => t.Id));
    }

    private void SetUpdateAllWait(CancellationTokenSource? wait, string? text)
    {
        _updateAllWait = wait;
        _updateAllWaitText = text;
        OnUpdatesChanged();
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnUpdatesChanged()
    {
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdatesText));
        OnPropertyChanged(nameof(IsWaitingForUpdateAll));
    }

    /// <summary>起動直後の確認。XIVToolHub 自身に更新があれば、見落とさないようその場で聞く。</summary>
    public async Task StartupAsync()
    {
        await CheckUpdatesAsync();
        if (_selfUpdate is { } release
            && _dialogs.Confirm($"XIVToolHub の新しいバージョン {release.TagName} があります。今すぐ更新して再起動しますか？", "XIVToolHub の更新"))
        {
            await UpdateSelfAsync(confirmed: true);
        }
    }

    private Task InstallSelectedAsync() => InstallToolsAsync(Tools.Where(t => t.IsSelected).Select(t => t.Id));

    private async Task InstallToolsAsync(IEnumerable<string> ids)
    {
        List<ToolItemViewModel> targets;
        try
        {
            // 依存されるツールから順に処理する
            var order = _catalog.ResolveInstallOrder(ids);
            targets = [.. order.Select(d => Tools.First(t => t.Id == d.Id))];
        }
        catch (HubException ex)
        {
            _dialogs.ShowError(ex.Message);
            return;
        }
        if (targets.Count == 0)
        {
            return;
        }
        var preferences = AskLanguages(targets);
        if (preferences is null)
        {
            return;
        }

        IsBusy = true;
        foreach (var t in targets)
        {
            _installing.Add(t.Id);
        }
        OnPropertyChanged(nameof(IsInstalling));
        var succeeded = 0;
        var devModeEnabled = false;
        var requested = targets.Count;
        try
        {
            targets = await InstallPrerequisitesAsync(targets);
            // 1件ずつ処理し、失敗しても残りは続ける
            for (var index = 0; index < targets.Count; index++)
            {
                var item = targets[index];
                var position = index;
                item.IsWorking = true;
                item.Error = null;
                SetOverallProgress(position, targets.Count, item, "準備しています", 0);
                var progress = new Progress<InstallProgress>(p =>
                {
                    item.ProgressText = p.Stage;
                    item.Progress = p.Fraction ?? item.Progress;
                    SetOverallProgress(position, targets.Count, item, p.Stage, item.Progress);
                });
                try
                {
                    var result = await _installer.InstallAsync(item.Definition, force: false, preferences.GetValueOrDefault(item.Id), progress, CancellationToken.None);
                    var record = _installed.Find(item.Id);
                    item.SetPreferences(record?.Language, record?.FontId);
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
            _installing.Clear();
            OnPropertyChanged(nameof(IsInstalling));
            OverallProgress = 0;
            OverallProgressText = "";
            IsBusy = false;
        }
        RefreshVoicePacks(targets);

        var message = $"{requested} 件中 {succeeded} 件を処理しました。";
        if (devModeEnabled)
        {
            message += "\n\nDalamud の開発モードを有効にしました。次回ゲーム起動時から、インストールしたプラグインが読み込まれます。";
        }
        if (succeeded == requested)
        {
            _dialogs.ShowInfo(message);
        }
        else
        {
            _dialogs.ShowError(message + "\n\n失敗した理由はログを確認してください。");
        }
    }

    private void SetOverallProgress(int index, int count, ToolItemViewModel item, string stage, double fraction)
    {
        OverallProgress = (index + Math.Clamp(fraction, 0, 1)) / count;
        OverallProgressText = $"{index + 1}/{count}  {item.Name}: {stage}";
    }

    /// <summary>
    /// 初めてインストールする言語選択対応ツールの表示言語を聞く。キャンセルされたら null。
    /// 更新の場合は前回の設定を引き継ぐので聞かない。
    /// </summary>
    private Dictionary<string, ToolPreferences>? AskLanguages(IEnumerable<ToolItemViewModel> targets)
    {
        var result = new Dictionary<string, ToolPreferences>();
        var groups = targets
            .Where(t => t.Definition.SupportsLanguage && _installed.Find(t.Id) is null)
            .Select(t => t.Definition)
            .GroupBy(d => string.Join(",", d.Languages));
        // 対応言語が同じツールはまとめて1回だけ聞く
        foreach (var group in groups)
        {
            var tools = group.ToList();
            var language = _dialogs.ShowLanguageSelection(tools);
            if (language is null)
            {
                return null;
            }
            foreach (var tool in tools)
            {
                result[tool.Id] = new ToolPreferences(language, null);
            }
        }
        return result;
    }

    private void OpenToolSettings(ToolItemViewModel item)
    {
        if (_installed.Find(item.Id) is not { } record)
        {
            return;
        }
        if (_dialogs.ShowToolSettings(new ToolSettingsViewModel(item.Definition, record, _installer, _fonts, _dialogs))
            && _installed.Find(item.Id) is { } updated)
        {
            item.SetPreferences(updated.Language, updated.FontId);
            AddLog($"{item.Name}: {item.PreferencesText} に変更しました。次回の起動時に反映されます。");
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
        RefreshVoicePacks(targets);
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
        _bulkSelecting = true;
        try
        {
            foreach (var item in Tools)
            {
                item.IsSelected = selected;
            }
        }
        finally
        {
            _bulkSelecting = false;
        }
    }

    private void ReloadTools()
    {
        var previous = Tools.ToDictionary(t => t.Id);
        foreach (var item in Tools)
        {
            item.PropertyChanged -= OnToolPropertyChanged;
        }
        Tools.Clear();
        foreach (var definition in _catalog.Tools)
        {
            var dependencyNames = definition.DependsOn.Select(id => _catalog.Find(id)?.Name ?? id);
            var item = new ToolItemViewModel(definition, _installed.Find(definition.Id))
            {
                DependsOnText = string.Join("、", dependencyNames),
            };
            if (previous.TryGetValue(definition.Id, out var old))
            {
                item.LatestVersion = old.LatestVersion;
                item.IsSelected = old.IsSelected;
            }
            item.PropertyChanged += OnToolPropertyChanged;
            Tools.Add(item);
        }
        OnUpdatesChanged();
    }

    /// <summary>
    /// ツールを選んだら、そのツールが必要とするツールも選ぶ。依存ツールなしでは動かないため。
    /// </summary>
    private void OnToolPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolItemViewModel.State))
        {
            OnUpdatesChanged();
            CommandManager.InvalidateRequerySuggested();
            return;
        }
        if (e.PropertyName is nameof(ToolItemViewModel.IsSelected) or nameof(ToolItemViewModel.IsWorking))
        {
            CommandManager.InvalidateRequerySuggested();
        }
        if (e.PropertyName != nameof(ToolItemViewModel.IsSelected) || sender is not ToolItemViewModel { IsSelected: true } item || _bulkSelecting)
        {
            return;
        }
        // チェックボックスの値を反映している最中に選択を変えると画面に反映されないので、反映が終わってから聞く
        if (_ui is null)
        {
            ConfirmRelatedTools(item);
        }
        else
        {
            _ui.Post(_ => ConfirmRelatedTools(item), null);
        }
    }

    /// <summary>
    /// 選んだツールに必要なツール・一緒に使うツールのうち、まだ入っていないものを一緒に入れるか聞く。
    /// 必要なツールを断った場合は、そのツールだけでは動かないので選択を外す。
    /// </summary>
    private void ConfirmRelatedTools(ToolItemViewModel item)
    {
        if (!item.IsSelected)
        {
            return;
        }
        var required = item.Definition.DependsOn
            .Select(id => Tools.FirstOrDefault(t => t.Id == id))
            .OfType<ToolItemViewModel>()
            .Where(t => !t.IsInstalled && !t.IsSelected)
            .ToList();
        if (required.Count > 0)
        {
            var list = string.Join("\n", required.Select(t => "・" + t.Name));
            if (_dialogs.Confirm($"{item.Name} を使うには、次のツールが必要です。\n\n{list}\n\n同時にインストールしますか？", "必要なツール"))
            {
                foreach (var t in required)
                {
                    t.IsSelected = true;
                }
                AddLog($"{item.Name} に必要な {string.Join("、", required.Select(t => t.Name))} も選択しました。");
            }
            else
            {
                item.IsSelected = false;
                AddLog($"{item.Name} は必要なツールが無いと動かないため、選択を外しました。");
                return;
            }
        }
        var optional = item.Definition.Includes
            .Select(id => Tools.FirstOrDefault(t => t.Id == id))
            .OfType<ToolItemViewModel>()
            .Where(t => !t.IsInstalled && !t.IsSelected)
            .ToList();
        if (optional.Count > 0)
        {
            var list = string.Join("\n", optional.Select(t => "・" + t.Name));
            if (_dialogs.Confirm($"{item.Name} と一緒に使う、次のプラグインも同時にインストールしますか？\n（入れなくても {item.Name} は動きます）\n\n{list}", "一緒に使うプラグイン"))
            {
                foreach (var t in optional)
                {
                    t.IsSelected = true;
                }
                AddLog($"{item.Name} と一緒に使う {string.Join("、", optional.Select(t => t.Name))} も選択しました。");
            }
        }
    }

    /// <summary>
    /// 選んだツールの前提のソフト（VC++ ランタイム・XIVLauncher など）のうち、入っていないものを入れるか聞いて入れる。
    /// 断った、または入れられなかった前提を必要とするツールは、今回はインストールしない。
    /// </summary>
    private async Task<List<ToolItemViewModel>> InstallPrerequisitesAsync(List<ToolItemViewModel> targets)
    {
        if (_prerequisites is null)
        {
            return targets;
        }
        var missing = targets
            .SelectMany(t => _prerequisites.Missing(t.Definition).Select(p => (Prerequisite: p, Tool: t)))
            .GroupBy(x => x.Prerequisite.Id)
            .ToList();
        var skipped = new HashSet<string>();
        foreach (var group in missing)
        {
            var prerequisite = group.First().Prerequisite;
            var tools = group.Select(x => x.Tool).ToList();
            var names = string.Join("、", tools.Select(t => t.Name));
            var admin = prerequisite.NeedsAdmin ? "\n\nWindows の管理者の確認が表示されます。" : "";
            if (!_dialogs.Confirm($"{names} には「{prerequisite.Name}」が必要です。\n{prerequisite.Description}\n\n公式の配布元から取得してインストールしますか？{admin}", "前提のソフト"))
            {
                AddLog($"{prerequisite.Name} を入れないため、{names} のインストールを見送りました。");
                skipped.UnionWith(tools.Select(t => t.Id));
                continue;
            }
            OverallProgressText = $"前提のソフト: {prerequisite.Name} を準備しています";
            OverallProgress = 0;
            var progress = new Progress<double>(f =>
            {
                OverallProgress = f;
                OverallProgressText = $"前提のソフト: {prerequisite.Name} をダウンロードしています（{f:P0}）";
            });
            try
            {
                await _prerequisites.InstallAsync(prerequisite, progress, CancellationToken.None);
                AddLog($"{prerequisite.Name} をインストールしました。");
            }
            catch (HubException ex)
            {
                AddLog(ex.Message);
                foreach (var t in tools)
                {
                    t.Error = ex.Message;
                }
                skipped.UnionWith(tools.Select(t => t.Id));
            }
        }
        return [.. targets.Where(t => !skipped.Contains(t.Id))];
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
