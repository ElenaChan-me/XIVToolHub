using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Catalog;

/// <summary>
/// 同梱のツール定義と、利用者が追加したツール定義をまとめて扱う。
/// </summary>
public sealed partial class ToolCatalog
{
    /// <summary>ACT プラグインの登録先になる ACT 本体の ID。</summary>
    public const string ActToolId = "act";
    public const string TriggeventToolId = "triggevent-jp";
    public const string VoicePackManifest = "manifest.json";

    private const string BuiltInResourceName = "XIVToolHub.Core.builtin-catalog.json";
    private const int MaxDependencies = 10;
    private const int ArgumentsMaxLength = 200;
    private const int NameMaxLength = 60;
    private const int DescriptionMaxLength = 500;
    private const int NotesMaxLength = 300;
    private const int PathMaxLength = 200;

    private readonly HubPaths _paths;
    private readonly List<ToolDefinition> _builtIn;
    private List<ToolDefinition> _userTools;

    public ToolCatalog(HubPaths paths)
    {
        _paths = paths;
        _builtIn = LoadBuiltIn();
        _userTools = LoadUserTools();
    }

    public IReadOnlyList<ToolDefinition> Tools => [.. _builtIn, .. _userTools];

    public ToolDefinition? Find(string id) => Tools.FirstOrDefault(t => t.Id == id);

    /// <summary>インストール済みのボイスパックのフォルダ（manifest.json のあるフォルダ）。</summary>
    public IReadOnlyList<string> VoicePackDirectories(InstalledStore installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        return [.. Tools
            .Where(t => t.Kind == ToolKind.VoicePack)
            .Select(t => (Tool: t, Record: installed.Find(t.Id)))
            .Where(x => x.Record is not null && File.Exists(Path.Combine(x.Record.InstallDirectory, x.Tool.EntryFile)))
            .Select(x => Path.GetDirectoryName(Path.GetFullPath(Path.Combine(x.Record!.InstallDirectory, x.Tool.EntryFile)))!)];
    }

    public void AddUserTool(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = Validate(definition);
        if (errors.Count > 0)
        {
            throw new HubException(string.Join(Environment.NewLine, errors));
        }
        if (Find(definition.Id) is not null)
        {
            throw new HubException($"ID「{definition.Id}」のツールは既に登録されています。");
        }

        var updated = new List<ToolDefinition>(_userTools) { definition with { IsBuiltIn = false } };
        JsonFiles.Write(_paths.UserCatalogFile, updated);
        _userTools = updated;
    }

    public void RemoveUserTool(string id)
    {
        if (_builtIn.Any(t => t.Id == id))
        {
            throw new HubException("同梱のツールはカタログから削除できません。");
        }
        var updated = _userTools.Where(t => t.Id != id).ToList();
        JsonFiles.Write(_paths.UserCatalogFile, updated);
        _userTools = updated;
    }

    public static IReadOnlyList<string> Validate(ToolDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new List<string>();

        if (!IdPattern().IsMatch(definition.Id ?? ""))
        {
            errors.Add("ID は半角英小文字・数字・ハイフンの 2〜50 文字で指定してください。");
        }
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Length > NameMaxLength)
        {
            errors.Add($"名前は 1〜{NameMaxLength} 文字で入力してください。");
        }
        if (definition.Description.Length > DescriptionMaxLength)
        {
            errors.Add($"説明は {DescriptionMaxLength} 文字以内にしてください。");
        }
        if (definition.Notes.Length > NotesMaxLength)
        {
            errors.Add($"補足は {NotesMaxLength} 文字以内にしてください。");
        }
        if (!Enum.IsDefined(definition.Kind))
        {
            errors.Add("種類の値が不正です。");
        }

        var source = definition.Source;
        if (source is null)
        {
            errors.Add("取得元が指定されていません。");
        }
        else
        {
            if (!OwnerPattern().IsMatch(source.Owner ?? ""))
            {
                errors.Add("GitHub のユーザー名（オーナー）が不正です。");
            }
            if (!RepoPattern().IsMatch(source.Repo ?? "") || source.Repo is "." or "..")
            {
                errors.Add("GitHub のリポジトリ名が不正です。");
            }
            if (!AssetPatternPattern().IsMatch(source.AssetPattern ?? ""))
            {
                errors.Add("アセット名は半角英数字と . _ - * ? の 100 文字以内で指定してください。");
            }
        }

        var entry = definition.EntryFile ?? "";
        if (!IsSafeRelativePath(entry))
        {
            errors.Add("起動ファイル・DLL は、展開先フォルダ内の相対パスで指定してください。");
        }
        else if (definition.Kind is ToolKind.DalamudPlugin or ToolKind.ActPlugin && !entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("プラグインは DLL ファイルを指定してください。");
        }
        else if (definition.Kind == ToolKind.Application && !entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("アプリは実行ファイル（.exe）を指定してください。");
        }
        else if (definition.Kind == ToolKind.VoicePack && !string.Equals(Path.GetFileName(entry), VoicePackManifest, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"ボイスパックは {VoicePackManifest} を指定してください。");
        }

        var dependsOn = definition.DependsOn ?? [];
        if (dependsOn.Count > MaxDependencies || dependsOn.Any(d => !IdPattern().IsMatch(d ?? "") || d == definition.Id))
        {
            errors.Add("依存するツールの指定が不正です。");
        }
        if (definition.Kind == ToolKind.ActPlugin && !dependsOn.Contains(ActToolId))
        {
            errors.Add($"ACT プラグインは ACT（{ActToolId}）に依存させてください。");
        }
        if (definition.Kind == ToolKind.VoicePack && !dependsOn.Contains(TriggeventToolId))
        {
            errors.Add($"ボイスパックは Triggevent Plus（{TriggeventToolId}）に依存させてください。");
        }

        var languages = definition.Languages ?? [];
        if (languages.Any(l => !ToolLanguages.IsSupported(l)) || languages.Distinct().Count() != languages.Count)
        {
            errors.Add("表示言語の指定が不正です。");
        }
        if (definition.SupportsFont && !languages.Contains(ToolLanguages.Japanese))
        {
            errors.Add("追加フォントは日本語表示に対応したツールだけで使えます。");
        }

        var arguments = definition.LaunchArguments ?? "";
        if (arguments.Length > ArgumentsMaxLength || arguments.Any(char.IsControl) || arguments.Contains('"', StringComparison.Ordinal))
        {
            errors.Add("起動時の引数が不正です。");
        }

        return errors;
    }

    /// <summary>
    /// 指定したツールと、その依存ツールをすべて含めて、依存される側が先になる順に並べる。
    /// </summary>
    public IReadOnlyList<ToolDefinition> ResolveInstallOrder(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var ordered = new List<ToolDefinition>();
        var done = new HashSet<string>();
        var visiting = new HashSet<string>();

        void Visit(string id, string? requiredBy)
        {
            if (done.Contains(id))
            {
                return;
            }
            var tool = Find(id) ?? throw new HubException(requiredBy is null
                ? $"ツール「{id}」がカタログにありません。"
                : $"「{requiredBy}」が必要とするツール「{id}」がカタログにありません。");
            if (!visiting.Add(id))
            {
                throw new HubException($"ツール「{id}」の依存関係が循環しています。");
            }
            foreach (var dependency in tool.DependsOn)
            {
                Visit(dependency, tool.Name);
            }
            visiting.Remove(id);
            done.Add(id);
            ordered.Add(tool);
        }

        foreach (var id in ids)
        {
            Visit(id, null);
        }
        return ordered;
    }

    /// <summary>
    /// 展開先から外へ出られない相対パスか。ZIP 内のパスやカタログの値をそのまま信用しないために使う。
    /// </summary>
    public static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > PathMaxLength || Path.IsPathRooted(path))
        {
            return false;
        }
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || path.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }
        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(s => s is not ("." or ".."));
    }

    private static List<ToolDefinition> LoadBuiltIn()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BuiltInResourceName)
            ?? throw new InvalidOperationException("同梱カタログが見つかりません。");
        var tools = JsonSerializer.Deserialize<List<ToolDefinition>>(stream, JsonFiles.Options) ?? [];
        return [.. tools.Select(t => t with { IsBuiltIn = true })];
    }

    private List<ToolDefinition> LoadUserTools()
    {
        var tools = JsonFiles.Read<List<ToolDefinition>>(_paths.UserCatalogFile) ?? [];
        var builtInIds = _builtIn.Select(t => t.Id).ToHashSet();
        // 手で編集されたファイルも信用せず、不正な定義や同梱と重複する定義は読み飛ばす
        return [.. tools
            .Where(t => Validate(t).Count == 0 && !builtInIds.Contains(t.Id))
            .DistinctBy(t => t.Id)
            .Select(t => t with { IsBuiltIn = false })];
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,49}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$")]
    private static partial Regex OwnerPattern();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepoPattern();

    [GeneratedRegex(@"^[A-Za-z0-9._*?\-]{1,100}$")]
    private static partial Regex AssetPatternPattern();
}
