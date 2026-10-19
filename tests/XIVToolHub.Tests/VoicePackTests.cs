using System.Text.Json;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Tests;

public class VoicePackTests
{
    private static ToolDefinition Pack(string id = "voicepack-test") => new()
    {
        Id = id,
        Name = "テストボイスパック",
        Kind = ToolKind.VoicePack,
        Source = new GitHubSource { Owner = "owner", Repo = "pack", AssetPattern = "voicepack-*.zip" },
        EntryFile = "manifest.json",
        DependsOn = [ToolCatalog.TriggeventToolId],
    };

    [Fact]
    public void Validate_RequiresManifestAndTriggevent()
    {
        Assert.Empty(ToolCatalog.Validate(Pack()));
        Assert.NotEmpty(ToolCatalog.Validate(Pack() with { EntryFile = "pack.dll" }));
        Assert.NotEmpty(ToolCatalog.Validate(Pack() with { DependsOn = [] }));
    }

    [Fact]
    public void BuiltInCatalogHasValidVoicePacks()
    {
        using var temp = new TempDirectory();
        var catalog = new ToolCatalog(temp.CreatePaths());
        var packs = catalog.Tools.Where(t => t.Kind == ToolKind.VoicePack).ToList();
        Assert.Equal(["voicepack-mia", "voicepack-tsukino"], packs.Select(p => p.Id));
        Assert.All(packs, p => Assert.Empty(ToolCatalog.Validate(p)));
    }

    [Fact]
    public async Task InstalledPacksAreWrittenIntoTriggeventSettings()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DalamudConfigPath)!);
        File.WriteAllText(paths.DalamudConfigPath, TestData.DalamudConfig(TestData.EmptyLocations, devMode: false));
        var handler = new FakeGitHubHandler();
        handler.Json("/repos/owner/pack/releases/latest", TestData.ReleaseJson("owner", "pack", "v1", (1, "voicepack-test.zip")));
        handler.Bytes("/repos/owner/pack/releases/assets/1", TestData.Zip(("manifest.json", "{}"), ("L_raid.wav", "RIFF")));
        handler.Json("/repos/owner/app/releases/latest", TestData.ReleaseJson("owner", "app", "r1", (2, "app-win.zip")));
        handler.Bytes("/repos/owner/app/releases/assets/2", TestData.Zip(("bin/app.exe", "exe")));

        var clock = new ManualClock();
        var game = new FakeGameDetector();
        var installed = new InstalledStore(paths);
        var catalog = new ToolCatalog(paths);
        var pack = Pack();
        catalog.AddUserTool(pack);
        var github = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var installer = new ToolInstaller(paths, github, installed, new DalamudConfigEditor(paths.DalamudConfigPath, game, clock),
            new ActConfigEditor(paths.ToolDirectory(ToolCatalog.ActToolId), new FakeGameDetector(), clock), game, clock,
            voicePacks: () => catalog.VoicePackDirectories(installed));
        var app = TestData.App() with { Languages = ["ja", "en"] };

        await installer.InstallAsync(app, false, null, CancellationToken.None);
        Assert.Empty(ReadPacks(app, paths.ToolDirectory(app.Id)));

        await installer.InstallAsync(pack, false, null, CancellationToken.None);
        var packDir = paths.ToolDirectory(pack.Id);
        Assert.Equal([Path.GetFullPath(packDir)], catalog.VoicePackDirectories(installed));
        installer.RefreshSettings(app);
        Assert.Equal([Path.GetFullPath(packDir)], ReadPacks(app, paths.ToolDirectory(app.Id)));

        installer.Uninstall(pack);
        installer.RefreshSettings(app);
        Assert.Empty(ReadPacks(app, paths.ToolDirectory(app.Id)));
    }

    private static List<string> ReadPacks(ToolDefinition tool, string installDirectory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ToolSettingsFile.PathFor(installDirectory, tool)));
        return [.. document.RootElement.GetProperty("voicePacks").EnumerateArray().Select(e => e.GetString()!)];
    }
}
