using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Install;

/// <summary>
/// インストール済みツールの記録を読み書きする。
/// </summary>
public sealed class InstalledStore
{
    private readonly HubPaths _paths;
    private Dictionary<string, InstalledTool> _items;

    public InstalledStore(HubPaths paths)
    {
        _paths = paths;
        var loaded = JsonFiles.Read<List<InstalledTool>>(paths.InstalledFile) ?? [];
        _items = loaded.DistinctBy(i => i.ToolId).ToDictionary(i => i.ToolId);
    }

    public InstalledTool? Find(string toolId) => _items.GetValueOrDefault(toolId);

    public IReadOnlyCollection<InstalledTool> All => _items.Values;

    public void Set(InstalledTool installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var updated = new Dictionary<string, InstalledTool>(_items) { [installed.ToolId] = installed };
        Save(updated);
    }

    public void Remove(string toolId)
    {
        if (!_items.ContainsKey(toolId))
        {
            return;
        }
        var updated = new Dictionary<string, InstalledTool>(_items);
        updated.Remove(toolId);
        Save(updated);
    }

    // ファイルへの保存に成功してからメモリ上の状態を差し替える
    private void Save(Dictionary<string, InstalledTool> updated)
    {
        JsonFiles.Write(_paths.InstalledFile, updated.Values.OrderBy(i => i.ToolId, StringComparer.Ordinal).ToList());
        _items = updated;
    }
}
