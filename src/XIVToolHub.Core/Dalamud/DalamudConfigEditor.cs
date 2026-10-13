using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Dalamud;

public sealed record DevPluginRegistration(bool Changed, bool DevModeEnabled, string? BackupPath);

/// <summary>
/// Dalamud の設定ファイル（dalamudConfig.json）の「開発用プラグインの場所」を編集する。
/// 他の設定には一切触れず、変更前には必ずバックアップを取る。
/// </summary>
public sealed class DalamudConfigEditor
{
    // Dalamud は Newtonsoft.Json の型名付きで保存するため、新しく作る要素にも同じ型名を付ける
    private const string LocationTypeName = "Dalamud.Configuration.DevPluginLocationSettings, Dalamud";
    private const string ListKey = "DevPluginLoadLocations";
    private const string DevModeKey = "DevMode";
    private const string TypeKey = "$type";
    private const string ValuesKey = "$values";
    private const long MaxConfigBytes = 16 * 1024 * 1024;

    private readonly string _configPath;
    private readonly IProcessDetector _gameDetector;
    private readonly TimeProvider _clock;

    public DalamudConfigEditor(string configPath, IProcessDetector gameDetector, TimeProvider clock)
    {
        _configPath = configPath;
        _gameDetector = gameDetector;
        _clock = clock;
    }

    public string ConfigPath => _configPath;

    public bool ConfigExists => File.Exists(_configPath);

    public IReadOnlyList<string> GetRegisteredPaths()
    {
        if (!ConfigExists)
        {
            return [];
        }
        var root = Load();
        return [.. GetLocations(root, create: false)?.OfType<JsonObject>()
            .Select(o => o["Path"]?.GetValue<string>())
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!) ?? []];
    }

    /// <summary>
    /// DLL のパスを開発用プラグインの場所に追加し、開発モードを有効にする。登録済みなら有効化だけ行う。
    /// </summary>
    public DevPluginRegistration Register(string dllPath)
    {
        var fullPath = Path.GetFullPath(dllPath);
        EnsureEditable();
        var root = Load();
        var locations = GetLocations(root, create: true)!;
        var changed = false;

        var existing = locations.OfType<JsonObject>().FirstOrDefault(o => SamePath(o["Path"], fullPath));
        if (existing is null)
        {
            locations.Add(new JsonObject
            {
                [TypeKey] = LocationTypeName,
                ["Path"] = fullPath,
                ["IsEnabled"] = true,
                ["Nickname"] = null,
            });
            changed = true;
        }
        else if (!(existing["IsEnabled"] is JsonValue isEnabled && isEnabled.TryGetValue<bool>(out var on) && on))
        {
            existing["IsEnabled"] = true;
            changed = true;
        }

        var devModeWasEnabled = root[DevModeKey] is JsonValue v && v.TryGetValue<bool>(out var enabled) && enabled;
        if (!devModeWasEnabled)
        {
            // 開発モードが有効でないと、登録した場所のプラグインは読み込まれない
            root[DevModeKey] = true;
            changed = true;
        }

        string? backup = null;
        if (changed)
        {
            backup = Save(root);
        }
        return new DevPluginRegistration(changed, !devModeWasEnabled, backup);
    }

    /// <summary>
    /// 登録を外す。開発モードの設定は利用者が他の用途で使っている可能性があるため変更しない。
    /// </summary>
    public bool Unregister(string dllPath)
    {
        if (!ConfigExists)
        {
            return false;
        }
        var fullPath = Path.GetFullPath(dllPath);
        EnsureEditable();
        var root = Load();
        var locations = GetLocations(root, create: false);
        if (locations is null)
        {
            return false;
        }

        var targets = locations.OfType<JsonObject>().Where(o => SamePath(o["Path"], fullPath)).ToList();
        if (targets.Count == 0)
        {
            return false;
        }
        foreach (var target in targets)
        {
            locations.Remove(target);
        }
        Save(root);
        return true;
    }

    private void EnsureEditable()
    {
        if (!ConfigExists)
        {
            throw new HubException(
                $"Dalamud の設定ファイルが見つかりません。XIVLauncher で一度ゲームを起動してから、もう一度お試しください。\n{_configPath}");
        }
        // ゲーム起動中は Dalamud が設定を保存し直すため、書き換えても上書きされてしまう
        if (_gameDetector.IsRunning())
        {
            throw new HubException("ゲームの起動中は Dalamud の設定を変更できません。ゲームを終了してからお試しください。");
        }
    }

    private JsonObject Load()
    {
        var info = new FileInfo(_configPath);
        if (info.Length > MaxConfigBytes)
        {
            throw new HubException("Dalamud の設定ファイルが大きすぎます。");
        }
        try
        {
            return JsonNode.Parse(File.ReadAllText(_configPath)) as JsonObject
                ?? throw new HubException("Dalamud の設定ファイルの形式が想定と異なります。");
        }
        catch (JsonException ex)
        {
            throw new HubException("Dalamud の設定ファイルを読み込めません。ファイルが壊れている可能性があります。", ex);
        }
    }

    private string Save(JsonObject root)
    {
        var stamp = _clock.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var backup = $"{_configPath}.xivtoolhub-{stamp}.bak";
        // 複数のプラグインを続けて登録すると同じ時刻になり得るため、既存のバックアップは上書きせず番号を付ける
        for (var i = 1; File.Exists(backup); i++)
        {
            backup = $"{_configPath}.xivtoolhub-{stamp}-{i}.bak";
        }
        File.Copy(_configPath, backup, overwrite: false);
        AtomicFile.WriteAllText(_configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return backup;
    }

    /// <summary>
    /// 一覧は型名付きの {"$type": ..., "$values": [...]} 形式で保存されている。素の配列にも対応しておく。
    /// </summary>
    private static JsonArray? GetLocations(JsonObject root, bool create)
    {
        switch (root[ListKey])
        {
            case JsonObject wrapper when wrapper[ValuesKey] is JsonArray values:
                return values;
            case JsonArray array:
                return array;
            case null when create:
                var created = new JsonArray();
                root[ListKey] = new JsonObject
                {
                    [TypeKey] = "System.Collections.Generic.List`1[[" + LocationTypeName + "]], System.Private.CoreLib",
                    [ValuesKey] = created,
                };
                return created;
            case null:
                return null;
            default:
                throw new HubException("Dalamud の設定ファイルの開発用プラグイン欄の形式が想定と異なります。");
        }
    }

    private static bool SamePath(JsonNode? node, string fullPath)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var path) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        try
        {
            return string.Equals(Path.GetFullPath(path), fullPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
