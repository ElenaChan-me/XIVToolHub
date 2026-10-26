namespace XIVToolHub.Core.Models;

public enum ToolKind
{
    /// <summary>Dalamud の開発用プラグインとして読み込ませるもの。</summary>
    DalamudPlugin,

    /// <summary>単体で起動するアプリ。</summary>
    Application,

    /// <summary>XIVToolHub が管理する ACT（Advanced Combat Tracker）に読み込ませるプラグイン。</summary>
    ActPlugin,

    /// <summary>Triggevent Plus の読み上げを差し替える音声のパック。インストール先を Triggevent Plus に伝える。</summary>
    VoicePack,
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

    /// <summary>先にインストールしておく必要があるツールの ID。</summary>
    public IReadOnlyList<string> DependsOn { get; init; } = [];

    /// <summary>
    /// 一緒に入れると便利なツールの ID。選ぶと一緒に選ばれるが、必須ではないので選択を外せる（ACT と FF14 用のプラグインなど）。
    /// </summary>
    public IReadOnlyList<string> Includes { get; init; } = [];

    /// <summary>アプリを起動するときの引数。</summary>
    public string LaunchArguments { get; init; } = "";

    /// <summary>選べる表示言語のコード。空なら言語選択に対応していない。</summary>
    public IReadOnlyList<string> Languages { get; init; } = [];

    /// <summary>日本語表示のときに追加フォントを選べるか。</summary>
    public bool SupportsFont { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool SupportsLanguage => Languages.Count > 0;

    /// <summary>アプリに同梱した定義か。同梱定義はカタログから削除できない。</summary>
    public bool IsBuiltIn { get; init; }
}
