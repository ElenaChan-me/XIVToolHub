using System.Collections.ObjectModel;
using System.Windows.Input;
using XIVToolHub.App.Infrastructure;
using XIVToolHub.App.Services;
using XIVToolHub.Core;
using XIVToolHub.Core.Fonts;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.ViewModels;

/// <summary>
/// フォント一覧の1行。Definition が null の行は「標準フォント」。
/// </summary>
public sealed class FontItemViewModel : ObservableObject
{
    private string? _filePath;

    public FontItemViewModel(FontDefinition? definition, string? filePath)
    {
        Definition = definition;
        _filePath = filePath;
    }

    public FontDefinition? Definition { get; }

    public string? Id => Definition?.Id;

    public bool IsStandard => Definition is null;

    public string Name => Definition?.Name ?? "標準フォント";

    public string Detail => Definition is null
        ? "ツールに組み込まれたフォントで表示します。"
        : $"作者: {Definition.Author}　／　{Definition.License}　／　収録文字: {Definition.Coverage}";

    public string? Warning => Definition?.Warning;

    public bool HasWarning => Warning is not null;

    public bool CanDownload => Definition?.CanDownload == true;

    public bool HasOfficialPage => Definition is not null;

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsAvailable => IsStandard || _filePath is not null;

    public string StatusText => IsStandard ? "" : IsAvailable ? "取得済み" : CanDownload ? "未取得" : "未取得（公式ページから入手して取り込み）";
}

/// <summary>
/// インストール済みツールの表示言語とフォントを変更する画面。
/// </summary>
public sealed class ToolSettingsViewModel : ObservableObject
{
    private readonly ToolDefinition _tool;
    private readonly ToolInstaller _installer;
    private readonly FontStore _fonts;
    private readonly IDialogService _dialogs;
    private string _selectedLanguage;
    private FontItemViewModel? _selectedFont;
    private string _message = "";
    private bool _isBusy;

    public ToolSettingsViewModel(ToolDefinition tool, InstalledTool installed, ToolInstaller installer, FontStore fonts, IDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(installed);
        _tool = tool;
        _installer = installer;
        _fonts = fonts;
        _dialogs = dialogs;

        Languages = [.. ToolLanguages.All.Where(l => tool.Languages.Contains(l.Code))];
        _selectedLanguage = installed.Language is { } current && tool.Languages.Contains(current)
            ? current
            : ToolPreferences.DefaultFor(tool).Language;

        Fonts.Add(new FontItemViewModel(null, null));
        if (tool.SupportsFont)
        {
            foreach (var font in FontCatalog.Fonts)
            {
                Fonts.Add(new FontItemViewModel(font, fonts.FindFontFile(font)));
            }
        }
        _selectedFont = Fonts.FirstOrDefault(f => f.Id == installed.FontId) ?? Fonts[0];

        DownloadCommand = new AsyncRelayCommand<FontItemViewModel>(DownloadAsync, f => !_isBusy && f.CanDownload);
        ImportCommand = new RelayCommand<FontItemViewModel>(Import, f => !_isBusy && !f.IsStandard);
        OpenPageCommand = new RelayCommand<FontItemViewModel>(f => _dialogs.OpenWebPage(f.Definition!.OfficialUrl), f => f.HasOfficialPage);
    }

    public string Title => $"{_tool.Name} の表示設定";

    public IReadOnlyList<LanguageOption> Languages { get; }

    public ObservableCollection<FontItemViewModel> Fonts { get; } = [];

    public ICommand DownloadCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand OpenPageCommand { get; }

    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
            {
                OnPropertyChanged(nameof(IsFontEnabled));
            }
        }
    }

    public bool SupportsFont => _tool.SupportsFont;

    /// <summary>追加フォントは日本語の文字を前提にしているため、日本語表示のときだけ選べる。</summary>
    public bool IsFontEnabled => _tool.SupportsFont && _selectedLanguage == ToolLanguages.Japanese;

    public FontItemViewModel? SelectedFont { get => _selectedFont; set => SetProperty(ref _selectedFont, value); }

    public string Message { get => _message; private set => SetProperty(ref _message, value); }

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

    /// <summary>設定を保存する。保存できたら true。</summary>
    public bool Save()
    {
        var font = IsFontEnabled ? _selectedFont : null;
        if (font is { IsAvailable: false })
        {
            Message = $"{font.Name} はまだ取得していません。ダウンロードするか、ファイルから取り込んでください。";
            return false;
        }
        try
        {
            _installer.ApplySettings(_tool, new ToolPreferences(_selectedLanguage, font?.Id));
            return true;
        }
        catch (HubException ex)
        {
            Message = ex.Message;
            return false;
        }
    }

    private async Task DownloadAsync(FontItemViewModel item)
    {
        IsBusy = true;
        Message = $"{item.Name} をダウンロードしています...";
        try
        {
            item.FilePath = await _fonts.DownloadAsync(item.Definition!, null, CancellationToken.None);
            SelectedFont = item;
            Message = $"{item.Name} を取得しました。";
        }
        catch (HubException ex)
        {
            Message = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Import(FontItemViewModel item)
    {
        var path = _dialogs.PickFontFile();
        if (path is null)
        {
            return;
        }
        try
        {
            item.FilePath = _fonts.Import(item.Definition!, path);
            SelectedFont = item;
            Message = $"{item.Name} を取り込みました。";
        }
        catch (HubException ex)
        {
            Message = ex.Message;
        }
    }
}
