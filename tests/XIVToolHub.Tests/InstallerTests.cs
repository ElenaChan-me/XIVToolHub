using System.Net;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Tests;

public class InstallerTests
{
    [Fact]
    public async Task InstallPlugin_DownloadsExtractsRegistersAndRecords()
    {
        using var env = new Env();
        env.PublishPlugin("v1.0", ("TestPlugin.dll", "dll-v1"), ("TestPlugin.json", "{}"));

        var result = await env.Installer.InstallAsync(TestData.Plugin(), force: false, null, CancellationToken.None);

        Assert.Equal("v1.0", result.Version);
        Assert.False(result.AlreadyLatest);
        Assert.True(result.DevModeEnabled);
        var dll = Path.Combine(env.Paths.ToolDirectory("test-plugin"), "TestPlugin.dll");
        Assert.Equal("dll-v1", File.ReadAllText(dll));
        Assert.Equal([dll], env.Dalamud.GetRegisteredPaths());
        Assert.Equal("v1.0", new InstalledStore(env.Paths).Find("test-plugin")!.Version);
        Assert.Empty(Directory.EnumerateFileSystemEntries(env.Paths.DownloadsDirectory));
    }

    [Fact]
    public async Task Install_SkipsWhenLatestAndUpdatesWhenNewer()
    {
        using var env = new Env();
        env.PublishPlugin("v1.0", ("TestPlugin.dll", "dll-v1"), ("TestPlugin.json", "{}"));
        await env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None);

        Assert.True((await env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None)).AlreadyLatest);

        env.PublishPlugin("v2.0", ("TestPlugin.dll", "dll-v2"), ("TestPlugin.json", "{}"));
        var updated = await env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None);

        Assert.Equal("v2.0", updated.Version);
        Assert.Equal("dll-v2", File.ReadAllText(Path.Combine(env.Paths.ToolDirectory("test-plugin"), "TestPlugin.dll")));
        Assert.Single(env.Dalamud.GetRegisteredPaths());
        Assert.Single(Directory.GetDirectories(env.Paths.ToolsDirectory));
    }

    [Fact]
    public async Task InstallApp_SelectsAssetByPatternAndDoesNotTouchDalamud()
    {
        using var env = new Env();
        env.Handler.Json("/repos/owner/app/releases/latest", TestData.ReleaseJson("owner", "app", "r5", (1, "app-linux.tar"), (2, "app-win.zip")));
        env.Handler.Bytes("/repos/owner/app/releases/assets/2", TestData.Zip(("bin/app.exe", "exe")));
        var configBefore = File.ReadAllText(env.Paths.DalamudConfigPath);

        await env.Installer.InstallAsync(TestData.App(), false, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(env.Paths.ToolDirectory("test-app"), "bin", "app.exe")));
        Assert.Equal(configBefore, File.ReadAllText(env.Paths.DalamudConfigPath));
        Assert.Null(new InstalledStore(env.Paths).Find("test-app")!.RegisteredPluginPath);
    }

    [Fact]
    public async Task Install_FailureKeepsPreviousVersion()
    {
        using var env = new Env();
        env.PublishPlugin("v1.0", ("TestPlugin.dll", "dll-v1"), ("TestPlugin.json", "{}"));
        await env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None);

        // マニフェストがない新バージョンは失敗させる
        env.PublishPlugin("v2.0", ("TestPlugin.dll", "dll-v2"));
        await Assert.ThrowsAsync<HubException>(() => env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None));

        Assert.Equal("dll-v1", File.ReadAllText(Path.Combine(env.Paths.ToolDirectory("test-plugin"), "TestPlugin.dll")));
        Assert.Equal("v1.0", new InstalledStore(env.Paths).Find("test-plugin")!.Version);
        Assert.False(Directory.Exists(env.Paths.StagingDirectory) && Directory.EnumerateFileSystemEntries(env.Paths.StagingDirectory).Any());
    }

    [Fact]
    public async Task Install_RejectsZipSlipAndMissingEntry()
    {
        using var env = new Env();
        env.PublishPlugin("v1.0", ("../evil.dll", "x"), ("TestPlugin.json", "{}"));
        await Assert.ThrowsAsync<HubException>(() => env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(env.Paths.DataDirectory, "staging", "evil.dll")));

        env.PublishPlugin("v1.1", ("Other.dll", "x"), ("Other.json", "{}"));
        await Assert.ThrowsAsync<HubException>(() => env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None));
        Assert.Null(new InstalledStore(env.Paths).Find("test-plugin"));
    }

    [Fact]
    public async Task InstallPlugin_RefusedWhileGameRunning()
    {
        using var env = new Env();
        env.PublishPlugin("v1.0", ("TestPlugin.dll", "dll"), ("TestPlugin.json", "{}"));
        env.Game.Running = true;

        await Assert.ThrowsAsync<HubException>(() => env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None));
        Assert.Empty(env.Handler.Requests);
    }

    [Fact]
    public async Task Install_PrivateRepoWithoutToken_ExplainsWhy()
    {
        using var env = new Env();

        var ex = await Assert.ThrowsAsync<HubException>(() => env.Installer.InstallAsync(TestData.App(), false, null, CancellationToken.None));
        Assert.Contains("トークン", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_IsSentAsBearer()
    {
        using var env = new Env(token: "secret-token");
        env.PublishPlugin("v1.0", ("TestPlugin.dll", "dll"), ("TestPlugin.json", "{}"));

        await env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None);

        Assert.All(env.Handler.Requests, r => Assert.Equal("Bearer secret-token", r.Headers.Authorization!.ToString()));
    }

    [Fact]
    public async Task Uninstall_RemovesFilesRegistrationAndRecord()
    {
        using var env = new Env();
        env.PublishPlugin("v1.0", ("TestPlugin.dll", "dll"), ("TestPlugin.json", "{}"));
        await env.Installer.InstallAsync(TestData.Plugin(), false, null, CancellationToken.None);

        env.Installer.Uninstall(TestData.Plugin());

        Assert.False(Directory.Exists(env.Paths.ToolDirectory("test-plugin")));
        Assert.Empty(env.Dalamud.GetRegisteredPaths());
        Assert.Null(new InstalledStore(env.Paths).Find("test-plugin"));
    }

    [Theory]
    [InlineData("latest.zip", "latest.zip", true)]
    [InlineData("LATEST.ZIP", "latest.zip", true)]
    [InlineData("app-win-1.2.zip", "app-win-*.zip", true)]
    [InlineData("app-linux.zip", "app-win-*.zip", false)]
    [InlineData("a1.zip", "a?.zip", true)]
    [InlineData("a12.zip", "a?.zip", false)]
    public void MatchesPattern_SupportsWildcards(string name, string pattern, bool expected)
    {
        Assert.Equal(expected, GitHubReleaseClient.MatchesPattern(name, pattern));
    }

    [Fact]
    public async Task Download_RejectsNonGitHubUrl()
    {
        using var env = new Env();
        var asset = new ReleaseAsset(1, "x.zip", 10, "https://evil.example.com/x.zip");

        await Assert.ThrowsAsync<HubException>(() =>
            env.Client.DownloadAssetAsync(TestData.App().Source, asset, Path.Combine(env.Temp.Path, "x.zip"), null, CancellationToken.None));
    }

    private sealed class Env : IDisposable
    {
        public Env(string? token = null)
        {
            Paths = Temp.CreatePaths();
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.DalamudConfigPath)!);
            File.WriteAllText(Paths.DalamudConfigPath, TestData.DalamudConfig(TestData.EmptyLocations, devMode: false));
            var http = new HttpClient(Handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) };
            Client = new GitHubReleaseClient(http, () => token);
            var clock = new ManualClock();
            Dalamud = new DalamudConfigEditor(Paths.DalamudConfigPath, Game, clock);
            Act = new ActConfigEditor(Paths.ToolDirectory(ToolCatalog.ActToolId), ActProcess, clock);
            Installer = new ToolInstaller(Paths, Client, new InstalledStore(Paths), Dalamud, Act, Game, clock);
        }

        public TempDirectory Temp { get; } = new();

        public HubPaths Paths { get; }

        public FakeGitHubHandler Handler { get; } = new();

        public FakeGameDetector Game { get; } = new();

        public GitHubReleaseClient Client { get; }

        public DalamudConfigEditor Dalamud { get; }

        public FakeGameDetector ActProcess { get; } = new();

        public ActConfigEditor Act { get; }

        public ToolInstaller Installer { get; }

        private long _assetId = 100;

        public void PublishPlugin(string tag, params (string Name, string Content)[] files)
        {
            var id = ++_assetId;
            Handler.Json("/repos/owner/plugin/releases/latest", TestData.ReleaseJson("owner", "plugin", tag, (id, "latest.zip")));
            Handler.Bytes($"/repos/owner/plugin/releases/assets/{id}", TestData.Zip(files));
        }

        public void Dispose() => Temp.Dispose();
    }
}
