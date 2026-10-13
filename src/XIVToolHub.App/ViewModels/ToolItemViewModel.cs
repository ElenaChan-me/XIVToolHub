using XIVToolHub.App.Infrastructure;
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

    public ToolItemViewModel(ToolDefinition definition, InstalledTool? installed)
    {
        Definition = definition;
        _installedVersion = installed?.Version;
    }

    public ToolDefinition Definition { get; }

    public string Id => Definition.Id;

    public string Name => Definition.Name;

    public string Description => Definition.Description;

    public string Notes => Definition.Notes;

    public string KindLabel => Definition.Kind == ToolKind.DalamudPlugin ? "Dalamud プラグイン" : "アプリ";

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
    }
}
