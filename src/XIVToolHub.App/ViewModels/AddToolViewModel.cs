using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows.Input;
using XIVToolHub.App.Infrastructure;
using XIVToolHub.Core;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;

namespace XIVToolHub.App.ViewModels;

public sealed partial class AddToolViewModel : ObservableObject
{
    private readonly GitHubReleaseClient _github;
    private string _repositoryText = "";
    private string _name = "";
    private string _id = "";
    private ToolKind _kind = ToolKind.Application;
    private string _assetPattern = "";
    private string _entryFile = "";
    private string _description = "";
    private string _notes = "";
    private string _message = "";
    private string? _selectedAsset;

    public AddToolViewModel(GitHubReleaseClient github)
    {
        _github = github;
        LookupCommand = new AsyncRelayCommand(LookupAsync, () => TryParseRepository(_repositoryText, out _, out _));
    }

    public static IReadOnlyList<Option<ToolKind>> KindOptions { get; } =
    [
        new(ToolKind.Application, "アプリ（単体で起動）"),
        new(ToolKind.DalamudPlugin, "Dalamud プラグイン"),
        new(ToolKind.ActPlugin, "ACT プラグイン"),
    ];

    public ObservableCollection<string> Assets { get; } = [];

    public ICommand LookupCommand { get; }

    public string RepositoryText
    {
        get => _repositoryText;
        set
        {
            if (SetProperty(ref _repositoryText, value ?? "") && TryParseRepository(_repositoryText, out _, out var repo) && _id.Length == 0)
            {
                Id = SuggestId(repo);
                if (_name.Length == 0)
                {
                    Name = repo;
                }
            }
        }
    }

    public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }

    public string Id { get => _id; set => SetProperty(ref _id, value ?? ""); }

    public ToolKind Kind { get => _kind; set => SetProperty(ref _kind, value); }

    public string AssetPattern { get => _assetPattern; set => SetProperty(ref _assetPattern, value ?? ""); }

    public string EntryFile { get => _entryFile; set => SetProperty(ref _entryFile, value ?? ""); }

    public string Description { get => _description; set => SetProperty(ref _description, value ?? ""); }

    public string Notes { get => _notes; set => SetProperty(ref _notes, value ?? ""); }

    public string Message { get => _message; set => SetProperty(ref _message, value); }

    public string? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (SetProperty(ref _selectedAsset, value) && value is not null)
            {
                AssetPattern = value;
            }
        }
    }

    /// <summary>
    /// 入力内容から定義を作る。不正な場合は理由を Message に入れて null を返す。
    /// </summary>
    public ToolDefinition? Build()
    {
        if (!TryParseRepository(_repositoryText, out var owner, out var repo))
        {
            Message = "GitHub のリポジトリを「https://github.com/オーナー/リポジトリ」または「オーナー/リポジトリ」の形で入力してください。";
            return null;
        }

        var definition = new ToolDefinition
        {
            Id = _id.Trim(),
            Name = _name.Trim(),
            Description = _description.Trim(),
            Notes = _notes.Trim(),
            Kind = _kind,
            Source = new GitHubSource { Owner = owner, Repo = repo, AssetPattern = _assetPattern.Trim() },
            EntryFile = _entryFile.Trim().Replace('\\', '/'),
            // ACT プラグインは XIVToolHub が管理する ACT に登録するため、ACT を先に入れる
            DependsOn = _kind == ToolKind.ActPlugin ? [ToolCatalog.ActToolId] : [],
        };
        var errors = ToolCatalog.Validate(definition);
        if (errors.Count > 0)
        {
            Message = string.Join("\n", errors);
            return null;
        }
        return definition;
    }

    public static bool TryParseRepository(string text, out string owner, out string repo)
    {
        owner = "";
        repo = "";
        var match = RepositoryPattern().Match(text?.Trim() ?? "");
        if (!match.Success)
        {
            return false;
        }
        owner = match.Groups["owner"].Value;
        repo = match.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            repo = repo[..^4];
        }
        return repo.Length > 0 && repo is not ("." or "..");
    }

    private async Task LookupAsync()
    {
        if (!TryParseRepository(_repositoryText, out var owner, out var repo))
        {
            return;
        }
        Message = "リリースを確認しています...";
        Assets.Clear();
        try
        {
            var release = await _github.GetLatestReleaseAsync(new GitHubSource { Owner = owner, Repo = repo, AssetPattern = "*" }, CancellationToken.None);
            foreach (var asset in release.Assets)
            {
                Assets.Add(asset.Name);
            }
            Message = release.Assets.Count == 0
                ? $"最新リリース {release.TagName} にファイルがありません。"
                : $"最新リリース {release.TagName} のファイルから、インストールする ZIP を選んでください。";
        }
        catch (HubException ex)
        {
            Message = ex.Message;
        }
    }

    private static string SuggestId(string repo)
    {
        var id = InvalidIdChars().Replace(repo.ToLowerInvariant(), "-").Trim('-');
        return id.Length > 50 ? id[..50] : id;
    }

    [GeneratedRegex(@"^(?:https?://github\.com/)?(?<owner>[A-Za-z0-9][A-Za-z0-9-]{0,38})/(?<repo>[A-Za-z0-9._-]{1,100})/?$")]
    private static partial Regex RepositoryPattern();

    [GeneratedRegex("[^a-z0-9-]+")]
    private static partial Regex InvalidIdChars();
}
