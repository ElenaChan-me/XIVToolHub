using System.Xml.Linq;
using XIVToolHub.App.ViewModels;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Fonts;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Tests;

public class ActSupportTests
{
    [Fact]
    public void Register_CreatesMinimalConfigWhenActNeverStarted()
    {
        using var temp = new TempDirectory();
        var (editor, actDir) = CreateEditor(temp);
        var dll = temp.Combine("plugins", "A.dll");

        Assert.True(editor.Register(dll));

        var bytes = File.ReadAllBytes(editor.ConfigPath);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var plugin = XDocument.Load(editor.ConfigPath).Root!.Element("ActPlugins")!.Element("Plugin")!;
        Assert.Equal("True", plugin.Attribute("Enabled")!.Value);
        Assert.Equal(dll, plugin.Attribute("Path")!.Value);
        Assert.Empty(Directory.GetFiles(Path.Combine(actDir, ActConfigEditor.ConfigFolderName), "*.bak"));
    }

    [Fact]
    public void Register_KeepsOtherSettingsAndMatchesRelativePaths()
    {
        using var temp = new TempDirectory();
        var (editor, actDir) = CreateEditor(temp);
        // ポータブル版の ACT は終了時にプラグインのパスを相対パスで保存し直す
        WriteConfig(editor, """
            <Config>
                <SettingsSerializer>
                    <CheckBox Name="cbAutoLoadLogs" Value="True" />
                </SettingsSerializer>
                <ActPlugins>
                    <Plugin Enabled="False" Path="..\plugins\A.dll" />
                </ActPlugins>
                <XmlSubs />
            </Config>
            """);
        var dll = Path.GetFullPath(Path.Combine(actDir, "..", "plugins", "A.dll"));

        Assert.True(editor.Register(dll));
        Assert.False(editor.Register(dll));

        var root = XDocument.Load(editor.ConfigPath).Root!;
        Assert.Equal("True", root.Element("SettingsSerializer")!.Element("CheckBox")!.Attribute("Value")!.Value);
        Assert.NotNull(root.Element("XmlSubs"));
        var plugin = Assert.Single(root.Element("ActPlugins")!.Elements("Plugin"));
        Assert.Equal("True", plugin.Attribute("Enabled")!.Value);
        Assert.Equal([dll], editor.GetRegisteredPaths());
        Assert.Single(Directory.GetFiles(Path.Combine(actDir, ActConfigEditor.ConfigFolderName), "*.bak"));
    }

    [Fact]
    public void Unregister_RemovesOnlyTarget()
    {
        using var temp = new TempDirectory();
        var (editor, _) = CreateEditor(temp);
        var keep = temp.Combine("keep.dll");
        var remove = temp.Combine("remove.dll");
        editor.Register(keep);
        editor.Register(remove);

        Assert.True(editor.Unregister(remove));
        Assert.False(editor.Unregister(remove));
        Assert.Equal([keep], editor.GetRegisteredPaths());
    }

    [Fact]
    public void Edit_IsRefusedWhileActRunningOrNotInstalled()
    {
        using var temp = new TempDirectory();
        var running = new FakeGameDetector { Running = true };
        var actDir = temp.Combine("act");
        Directory.CreateDirectory(actDir);
        var editor = new ActConfigEditor(actDir, running, new ManualClock());
        Assert.Throws<HubException>(() => editor.Register(temp.Combine("a.dll")));
        Assert.False(File.Exists(editor.ConfigPath));

        var missing = new ActConfigEditor(temp.Combine("none"), new FakeGameDetector(), new ManualClock());
        Assert.Throws<HubException>(() => missing.Register(temp.Combine("a.dll")));
    }

    [Theory]
    [InlineData("<!DOCTYPE Config [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]><Config>&x;</Config>")]
    [InlineData("<NotConfig />")]
    [InlineData("<Config>")]
    public void BrokenOrUnsafeConfig_IsRejectedAndNotOverwritten(string content)
    {
        using var temp = new TempDirectory();
        var (editor, _) = CreateEditor(temp);
        WriteConfig(editor, content);

        Assert.Throws<HubException>(() => editor.Register(temp.Combine("a.dll")));
        Assert.Equal(content, File.ReadAllText(editor.ConfigPath));
    }

    [Fact]
    public void ResolveInstallOrder_PutsDependenciesFirstWithoutDuplicates()
    {
        using var temp = new TempDirectory();
        var catalog = new ToolCatalog(temp.CreatePaths());

        var order = catalog.ResolveInstallOrder(["triggevent-jp", "overlayplugin"]).Select(t => t.Id).ToList();

        Assert.Equal(["act", "ffxiv-act-plugin", "overlayplugin", "triggevent-jp"], order);
    }

    [Fact]
    public void ResolveInstallOrder_RejectsMissingDependencyAndCycles()
    {
        using var temp = new TempDirectory();
        var catalog = new ToolCatalog(temp.CreatePaths());
        catalog.AddUserTool(TestData.App("needs-missing") with { DependsOn = ["no-such-tool"] });
        Assert.Throws<HubException>(() => catalog.ResolveInstallOrder(["needs-missing"]));

        catalog.AddUserTool(TestData.App("cycle-a") with { DependsOn = ["cycle-b"] });
        catalog.AddUserTool(TestData.App("cycle-b") with { DependsOn = ["cycle-a"] });
        Assert.Throws<HubException>(() => catalog.ResolveInstallOrder(["cycle-a"]));
    }

    [Fact]
    public void Validate_RequiresActDependencyForActPluginsAndSafeArguments()
    {
        var plugin = TestData.Plugin() with { Kind = ToolKind.ActPlugin };
        Assert.NotEmpty(ToolCatalog.Validate(plugin));
        Assert.Empty(ToolCatalog.Validate(plugin with { DependsOn = [ToolCatalog.ActToolId] }));
        Assert.NotEmpty(ToolCatalog.Validate(TestData.App() with { LaunchArguments = "\"evil\"" }));
        Assert.NotEmpty(ToolCatalog.Validate(TestData.App() with { DependsOn = ["test-app"] }));
    }

    [Fact]
    public async Task InstallActPlugin_RegistersToHubManagedAct()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        var handler = new FakeGitHubHandler();
        handler.Json("/repos/owner/act/releases/latest", TestData.ReleaseJson("owner", "act", "3.8", (1, "ACTv3.zip")));
        handler.Bytes("/repos/owner/act/releases/assets/1", TestData.Zip(("Advanced Combat Tracker.exe", "exe")));
        handler.Json("/repos/owner/ffxiv/releases/latest", TestData.ReleaseJson("owner", "ffxiv", "3.0", (2, "FFXIV_ACT_Plugin_3.0.zip")));
        handler.Bytes("/repos/owner/ffxiv/releases/assets/2", TestData.Zip(("FFXIV_ACT_Plugin.dll", "dll")));
        var clock = new ManualClock();
        var game = new FakeGameDetector();
        var actProcess = new FakeGameDetector();
        var act = new ActConfigEditor(paths.ToolDirectory(ToolCatalog.ActToolId), actProcess, clock);
        var github = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var installer = new ToolInstaller(paths, github, new InstalledStore(paths), new DalamudConfigEditor(paths.DalamudConfigPath, game, clock), act, game, clock);
        var actTool = TestData.App("act") with { Source = new GitHubSource { Owner = "owner", Repo = "act", AssetPattern = "ACTv3.zip" }, EntryFile = "Advanced Combat Tracker.exe", LaunchArguments = "-portable" };
        var plugin = TestData.Plugin("ffxiv-act-plugin") with
        {
            Kind = ToolKind.ActPlugin,
            Source = new GitHubSource { Owner = "owner", Repo = "ffxiv", AssetPattern = "FFXIV_ACT_Plugin_*.zip" },
            EntryFile = "FFXIV_ACT_Plugin.dll",
            DependsOn = [ToolCatalog.ActToolId],
        };

        await Assert.ThrowsAsync<HubException>(() => installer.InstallAsync(plugin, false, null, CancellationToken.None));

        await installer.InstallAsync(actTool, false, null, CancellationToken.None);
        actProcess.Running = true;
        await Assert.ThrowsAsync<HubException>(() => installer.InstallAsync(plugin, false, null, CancellationToken.None));
        actProcess.Running = false;
        await installer.InstallAsync(plugin, false, null, CancellationToken.None);

        var dll = Path.Combine(paths.ToolDirectory("ffxiv-act-plugin"), "FFXIV_ACT_Plugin.dll");
        Assert.Equal([dll], act.GetRegisteredPaths());
        Assert.Equal("-portable", actTool.LaunchArguments);

        installer.Uninstall(plugin);
        Assert.Empty(act.GetRegisteredPaths());
        Assert.False(Directory.Exists(paths.ToolDirectory("ffxiv-act-plugin")));
    }

    [Fact]
    public void SelectingTool_AlsoSelectsDependencies()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        var clock = new ManualClock();
        var game = new FakeGameDetector();
        var catalog = new ToolCatalog(paths);
        var installed = new InstalledStore(paths);
        var github = new GitHubReleaseClient(new HttpClient(new FakeGitHubHandler()) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var installer = new ToolInstaller(paths, github, installed, new DalamudConfigEditor(paths.DalamudConfigPath, game, clock),
            new ActConfigEditor(paths.ToolDirectory(ToolCatalog.ActToolId), game, clock), game, clock);
        var vm = new MainViewModel(catalog, installed, installer, new ToolLauncher(paths), new FontStore(paths, new HttpClient()), new HubSettingsStore(paths), _ => { }, paths, new NullDialogs(), clock);

        vm.Tools.Single(t => t.Id == "triggevent-jp").IsSelected = true;

        Assert.Equal(
            ["act", "ffxiv-act-plugin", "overlayplugin", "triggevent-jp"],
            vm.Tools.Where(t => t.IsSelected).Select(t => t.Id).Order());
        Assert.Contains("ACT", vm.Tools.Single(t => t.Id == "triggevent-jp").DependsOnText, StringComparison.Ordinal);
        Assert.Equal("ACT プラグイン", vm.Tools.Single(t => t.Id == "overlayplugin").KindLabel);
    }

    private static (ActConfigEditor Editor, string ActDir) CreateEditor(TempDirectory temp)
    {
        var actDir = temp.Combine("tools", "act");
        Directory.CreateDirectory(actDir);
        return (new ActConfigEditor(actDir, new FakeGameDetector(), new ManualClock()), actDir);
    }

    private static void WriteConfig(ActConfigEditor editor, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(editor.ConfigPath)!);
        File.WriteAllText(editor.ConfigPath, content);
    }

    private sealed class NullDialogs : App.Services.IDialogService
    {
        public void ShowInfo(string message)
        {
        }

        public void ShowError(string message)
        {
        }

        public bool Confirm(string message, string title = "確認") => false;

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

        public string? ShowLanguageSelection(IReadOnlyList<ToolDefinition> tools) => null;

        public bool ShowToolSettings(ToolSettingsViewModel viewModel) => false;

        public string? PickFontFile() => null;

        public void OpenWebPage(string url)
        {
        }
    }
}
