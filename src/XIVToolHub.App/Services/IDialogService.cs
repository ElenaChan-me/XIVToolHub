using XIVToolHub.App.ViewModels;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.Services;

public interface IDialogService
{
    void ShowInfo(string message);

    void ShowError(string message);

    bool Confirm(string message, string title = "確認");

    /// <summary>ツール追加画面を開く。キャンセル時は null。</summary>
    ToolDefinition? ShowAddTool();

    void ShowTokenSettings();

    /// <summary>新しくインストールするツールの表示言語を選ぶ。キャンセル時は null。</summary>
    string? ShowLanguageSelection(IReadOnlyList<ToolDefinition> tools);

    /// <summary>表示言語・フォントの設定画面を開く。保存したら true。</summary>
    bool ShowToolSettings(ToolSettingsViewModel viewModel);

    /// <summary>取り込むフォントファイルを選ぶ。キャンセル時は null。</summary>
    string? PickFontFile();

    /// <summary>フォントの公式ページなど、カタログに載せた Web ページを開く。</summary>
    void OpenWebPage(string url);

    void OpenFolder(string path);

    void OpenUrl(string url);
}
