using XIVToolHub.App.Services;
using XIVToolHub.App.ViewModels;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Tests;

public class AppTests
{
    [Theory]
    [InlineData("https://github.com/xpdota/event-trigger", "xpdota", "event-trigger")]
    [InlineData("https://github.com/owner/repo.git", "owner", "repo")]
    [InlineData("owner/repo/", "owner", "repo")]
    public void TryParseRepository_AcceptsCommonForms(string text, string owner, string repo)
    {
        Assert.True(AddToolViewModel.TryParseRepository(text, out var o, out var r));
        Assert.Equal(owner, o);
        Assert.Equal(repo, r);
    }

    [Theory]
    [InlineData("https://example.com/owner/repo")]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("")]
    public void TryParseRepository_RejectsOtherForms(string text)
    {
        Assert.False(AddToolViewModel.TryParseRepository(text, out _, out _));
    }

    [Fact]
    public void AddToolViewModel_BuildsValidDefinitionAndSuggestsId()
    {
        var vm = new AddToolViewModel(new GitHubReleaseClient(new HttpClient(), () => null))
        {
            RepositoryText = "https://github.com/Some-Owner/My_Tool",
            AssetPattern = "my-tool-win-*.zip",
            EntryFile = @"my-tool\my-tool.exe",
        };

        var definition = vm.Build();

        Assert.NotNull(definition);
        Assert.Equal("my-tool", definition!.Id);
        Assert.Equal("My_Tool", definition.Name);
        Assert.Equal("my-tool/my-tool.exe", definition.EntryFile);
        Assert.Equal(ToolKind.Application, definition.Kind);
    }

    [Fact]
    public void AddToolViewModel_ReportsValidationErrors()
    {
        var vm = new AddToolViewModel(new GitHubReleaseClient(new HttpClient(), () => null))
        {
            RepositoryText = "owner/repo",
            AssetPattern = "x.zip",
            EntryFile = "../x.exe",
        };

        Assert.Null(vm.Build());
        Assert.NotEmpty(vm.Message);
    }

    [Fact]
    public void Launcher_OnlyResolvesExecutableInsideToolFolder()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        var launcher = new ToolLauncher(paths);
        var app = TestData.App();

        Assert.Throws<HubException>(() => launcher.ResolveExecutable(app));
        var exe = Path.Combine(paths.ToolDirectory(app.Id), "bin", "app.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "");

        Assert.Equal(exe, launcher.ResolveExecutable(app));
        Assert.Throws<HubException>(() => launcher.ResolveExecutable(TestData.Plugin()));
        Assert.Throws<HubException>(() => launcher.ResolveExecutable(app with { EntryFile = "../../evil.exe" }));
    }

    [Fact]
    public async Task InstallSelected_ContinuesAfterFailureAndReportsResult()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        var handler = new FakeGitHubHandler();
        handler.Json("/repos/owner/app/releases/latest", TestData.ReleaseJson("owner", "app", "r1", (1, "app-win.zip")));
        handler.Bytes("/repos/owner/app/releases/assets/1", TestData.Zip(("bin/app.exe", "exe")));
        // 2つ目のツールはリリースが見つからず失敗する
        var catalog = new ToolCatalog(paths);
        catalog.AddUserTool(TestData.App("good-app"));
        catalog.AddUserTool(TestData.App("bad-app") with { Source = new GitHubSource { Owner = "owner", Repo = "missing", AssetPattern = "x.zip" } });

        var clock = new ManualClock();
        var game = new FakeGameDetector();
        var github = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var installed = new InstalledStore(paths);
        var installer = new ToolInstaller(paths, github, installed, new DalamudConfigEditor(paths.DalamudConfigPath, game, clock), new ActConfigEditor(paths.ToolDirectory(ToolCatalog.ActToolId), new FakeGameDetector(), clock), game, clock);
        var dialogs = new RecordingDialogs();
        var vm = new MainViewModel(catalog, installed, installer, new ToolLauncher(paths), paths, dialogs, clock);

        foreach (var item in vm.Tools.Where(t => !t.IsBuiltIn))
        {
            item.IsSelected = true;
        }
        vm.InstallSelectedCommand.Execute(null);
        await WaitUntil(() => dialogs.Errors.Count > 0);

        Assert.Equal("r1", vm.Tools.Single(t => t.Id == "good-app").InstalledVersion);
        Assert.True(vm.Tools.Single(t => t.Id == "bad-app").HasError);
        Assert.Contains("2 件中 1 件", dialogs.Errors.Single(), StringComparison.Ordinal);
        Assert.NotNull(installed.Find("good-app"));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
        }
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public List<string> Errors { get; } = [];

        public List<string> Infos { get; } = [];

        public void ShowInfo(string message) => Infos.Add(message);

        public void ShowError(string message) => Errors.Add(message);

        public bool Confirm(string message, string title = "確認") => true;

        public ToolDefinition? ShowAddTool() => null;

        public void ShowTokenSettings()
        {
        }

        public void OpenFolder(string path)
        {
        }

        public void OpenUrl(string url)
        {
        }
    }
}
