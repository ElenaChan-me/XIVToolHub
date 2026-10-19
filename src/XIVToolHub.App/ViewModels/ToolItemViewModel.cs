using XIVToolHub.App.Infrastructure;
using XIVToolHub.Core.Fonts;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.ViewModels;

public enum ToolState
{
    NotInstalled,
    Installed,
    UpdateAvailable,
    Unknown,
}

public sealed class ToolItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string? _installedVersion;
    private string? _latestVersion;
    private string _progressText = "";
    private double _progress;
    private bool _isWorking;
    private string? _error;
    private string? _language;
    private string? _fontId;

    public ToolItemViewModel(ToolDefinition definition, InstalledTool? installed)
    {
        Definition = definition;
        _installedVersion = installed?.Version;
        _language = installed?.Language;
        _fontId = installed?.FontId;
    }

    public ToolDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public string Description => Definition.Description;

    public string Notes => Definition.Notes;

    public string KindLabel => Definition.Kind switch
    {
        ToolKind.DalamudPlugin => "Dalamud プラグイン",
        ToolKind.ActPlugin => "ACT プラグイン",
        ToolKind.VoicePack => "ボイスパック",
        _ => "アプリ",
    };

    /// <summary>依存するツールの名前。画面表示用に MainViewModel が設定する。</summary>
    public string DependsOnText { get; init; } = "";

    public bool HasDependencies => DependsOnText.Length > 0;

    public string SourceLabel => $"{Definition.Source.Owner}/{Definition.Source.Repo}";

    public bool IsBuiltIn => Definition.IsBuiltIn;

    public bool IsApplication => Definition.Kind == ToolKind.Application;

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public string? InstalledVersion
    {
        get => _installedVersion;
        set
        {
            if (SetProperty(ref _installedVersion, value))
            {
                NotifyState();
            }
        }
    }

    public string? LatestVersion
    {
        get => _latestVersion;
        set
        {
            if (SetProperty(ref _latestVersion, value))
            {
                NotifyState();
            }
        }
    }

    public string? Error
    {
        get => _error;
        set
        {
            if (SetProperty(ref _error, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public bool IsInstalled => _installedVersion is not null;

    public ToolState State => _installedVersion switch
    {
        null => ToolState.NotInstalled,
        _ when _latestVersion is null => ToolState.Unknown,
        _ when _latestVersion != _installedVersion => ToolState.UpdateAvailable,
        _ => ToolState.Installed,
    };

    public string StateLabel => State switch
    {
        ToolState.NotInstalled => "未インストール",
        ToolState.UpdateAvailable => "更新あり",
        ToolState.Installed => "最新",
        _ => "インストール済み",
    };

    public string InstalledVersionText => _installedVersion ?? "-";

    public string LatestVersionText => _latestVersion ?? "-";

    /// <summary>表示言語・フォントを設定できるか。インストール後に設定ファイルを書き込むため、未インストールでは設定できない。</summary>
    public bool CanConfigure => Definition.SupportsLanguage && IsInstalled;

    public string PreferencesText
    {
        get
        {
            var text = $"表示言語: {ToolLanguages.DisplayName(_language)}";
            if (Definition.SupportsFont && _language == ToolLanguages.Japanese)
            {
                text += $"   フォント: {FontCatalog.Find(_fontId)?.Name ?? "標準"}";
            }
            return text;
        }
    }

    public void SetPreferences(string? language, string? fontId)
    {
        _language = language;
        _fontId = fontId;
        OnPropertyChanged(nameof(PreferencesText));
    }

    public bool IsWorking { get => _isWorking; set => SetProperty(ref _isWorking, value); }

    public double Progress { get => _progress; set => SetProperty(ref _progress, value); }

    public string ProgressText { get => _progressText; set => SetProperty(ref _progressText, value); }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(LatestVersionText));
        OnPropertyChanged(nameof(CanConfigure));
    }
}
