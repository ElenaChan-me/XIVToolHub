namespace XIVToolHub.Core.Storage;

/// <summary>
/// アプリが読み書きする場所。テストで差し替えられるよう、既定値の組み立てだけを static にしている。
/// </summary>
public sealed record HubPaths
{
    public const string AppFolderName = "XIVToolHub";

    /// <summary>ツール本体・ダウンロード・インストール記録を置く場所。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>利用者が追加したカタログなど、PC 間で持ち運べる設定を置く場所。</summary>
    public required string SettingsDirectory { get; init; }

    public required string DalamudConfigPath { get; init; }

    public string ToolsDirectory => Path.Combine(DataDirectory, "tools");

    public string DownloadsDirectory => Path.Combine(DataDirectory, "downloads");

    public string StagingDirectory => Path.Combine(DataDirectory, "staging");

    public string InstalledFile => Path.Combine(DataDirectory, "installed.json");

    public string UserCatalogFile => Path.Combine(SettingsDirectory, "catalog.json");

    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    public string ToolDirectory(string toolId) => Path.Combine(ToolsDirectory, toolId);

    public static HubPaths Default { get; } = new()
    {
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName),
        SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName),
        DalamudConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "dalamudConfig.json"),
    };
}
