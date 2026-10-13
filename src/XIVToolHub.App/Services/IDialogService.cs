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

    void OpenFolder(string path);

    void OpenUrl(string url);
}
