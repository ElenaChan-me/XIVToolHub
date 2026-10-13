namespace XIVToolHub.Core.Models;

/// <summary>
/// インストール済みツールの記録。
/// </summary>
public sealed record InstalledTool
{
    public required string ToolId { get; init; }

    /// <summary>インストールしたリリースのタグ名。</summary>
    public required string Version { get; init; }

    public required string AssetName { get; init; }

    public required string InstallDirectory { get; init; }

    /// <summary>Dalamud に登録した DLL のパス。アプリの場合は null。</summary>
    public string? RegisteredPluginPath { get; init; }

    public required DateTimeOffset InstalledAt { get; init; }
}
