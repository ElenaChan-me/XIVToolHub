namespace XIVToolHub.Core.Models;

public enum ToolKind
{
    /// <summary>Dalamud の開発用プラグインとして読み込ませるもの。</summary>
    DalamudPlugin,

    /// <summary>単体で起動するアプリ。</summary>
    Application,
}

/// <summary>
/// GitHub のリリースから取得するときの情報。
/// </summary>
public sealed record GitHubSource
{
    public required string Owner { get; init; }

    public required string Repo { get; init; }

    /// <summary>取得するアセット名。* と ? のワイルドカードが使える。</summary>
    public required string AssetPattern { get; init; }

    public bool IncludePrerelease { get; init; }

    public string RepositoryUrl => $"https://github.com/{Owner}/{Repo}";
}

/// <summary>
/// カタログに載せるツール1件分の定義。
/// </summary>
public sealed record ToolDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required ToolKind Kind { get; init; }

    public required GitHubSource Source { get; init; }

    /// <summary>
    /// 展開後のフォルダ内での相対パス。Dalamud プラグインは DLL、アプリは起動する実行ファイル。
    /// </summary>
    public required string EntryFile { get; init; }

    /// <summary>起動・利用に必要なものなど、利用者への補足。</summary>
    public string Notes { get; init; } = "";

    /// <summary>アプリに同梱した定義か。同梱定義はカタログから削除できない。</summary>
    public bool IsBuiltIn { get; init; }
}
