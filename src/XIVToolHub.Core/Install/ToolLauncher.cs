using System.Diagnostics;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Install;

/// <summary>
/// インストール済みのアプリを起動する。起動するのはツールのフォルダ内の実行ファイルだけに限る。
/// </summary>
public sealed class ToolLauncher(HubPaths paths)
{
    public string ResolveExecutable(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Kind != ToolKind.Application)
        {
            throw new HubException("Dalamud プラグインはゲーム内で読み込まれるため、ここからは起動できません。");
        }
        if (!ToolCatalog.IsSafeRelativePath(tool.EntryFile))
        {
            throw new HubException("起動ファイルの指定が不正です。");
        }

        var root = Path.GetFullPath(paths.ToolDirectory(tool.Id));
        var exe = Path.GetFullPath(Path.Combine(root, tool.EntryFile));
        if (!exe.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubException("起動ファイルの指定が不正です。");
        }
        if (!File.Exists(exe))
        {
            throw new HubException($"{tool.Name} の起動ファイルが見つかりません。インストールし直してください。");
        }
        return exe;
    }

    public void Launch(ToolDefinition tool)
    {
        var exe = ResolveExecutable(tool);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(exe)
            {
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = false,
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new HubException($"{tool.Name} を起動できませんでした。{ex.Message}", ex);
        }
    }
}
