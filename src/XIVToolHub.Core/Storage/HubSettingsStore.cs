using XIVToolHub.Core.Models;

namespace XIVToolHub.Core.Storage;

/// <summary>
/// XIVToolHub 自体の設定（%APPDATA%\XIVToolHub\settings.json）。
/// </summary>
public sealed class HubSettingsStore
{
    private readonly HubPaths _paths;
    private HubSettings _current;

    public HubSettingsStore(HubPaths paths)
    {
        _paths = paths;
        HubSettings? loaded;
        try
        {
            loaded = JsonFiles.Read<HubSettings>(paths.SettingsFile);
        }
        catch (HubException)
        {
            // 壊れた設定ファイルで起動できなくなるより、既定値で起動して保存し直すほうがよい
            loaded = null;
        }
        _current = new HubSettings { Theme = HubThemes.Normalize(loaded?.Theme) };
    }

    public string Theme => _current.Theme;

    public void SetTheme(string theme)
    {
        if (!HubThemes.IsSupported(theme))
        {
            throw new HubException("テーマの指定が不正です。");
        }
        var updated = _current with { Theme = theme };
        JsonFiles.Write(_paths.SettingsFile, updated);
        _current = updated;
    }

    private sealed record HubSettings
    {
        public string Theme { get; init; } = HubThemes.Default;
    }
}
