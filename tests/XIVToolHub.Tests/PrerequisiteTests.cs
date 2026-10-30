using System.Net;
using XIVToolHub.Core;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Prerequisites;

namespace XIVToolHub.Tests;

public class PrerequisiteTests
{
    private sealed class Fake
    {
        public HashSet<string> Installed { get; } = [];
        public List<(string Path, string Args)> Runs { get; } = [];
        public bool SignatureOk { get; set; } = true;
        public int ExitCode { get; set; }
        public bool InstallsOnRun { get; set; } = true;
    }

    private static (PrerequisiteManager Manager, Fake Fake, FakeGitHubHandler Handler) Create(TempDirectory temp)
    {
        var fake = new Fake();
        var handler = new FakeGitHubHandler();
        handler.Bytes("/vs/17/release/vc_redist.x64.exe", [1, 2, 3]);
        handler.Json("/repos/goatcorp/FFXIVQuickLauncher/releases/latest", TestData.ReleaseJson("goatcorp", "FFXIVQuickLauncher", "7.0.20", (9, "XIVLauncher-win-Setup.exe")));
        handler.Bytes("/repos/goatcorp/FFXIVQuickLauncher/releases/assets/9", [4, 5]);
        var http = new HttpClient(handler);
        var github = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var manager = new PrerequisiteManager(temp.CreatePaths(), http, github,
            probe: id => fake.Installed.Contains(id),
            run: (path, args, _) =>
            {
                fake.Runs.Add((path, args));
                Assert.True(File.Exists(path));
                if (fake.InstallsOnRun)
                {
                    fake.Installed.Add(Path.GetFileName(path).Split('-')[0] switch
                    {
                        "vcredist" => PrerequisiteCatalog.VcRedistId,
                        "netfx48" => PrerequisiteCatalog.NetFrameworkId,
                        _ => PrerequisiteCatalog.XivLauncherId,
                    });
                }
                return Task.FromResult(fake.ExitCode);
            },
            verifySignature: _ => fake.SignatureOk);
        return (manager, fake, handler);
    }

    private static ToolDefinition WithPrerequisites(params string[] ids) => TestData.App() with { Prerequisites = ids };

    [Fact]
    public void ListsOnlyMissingPrerequisites()
    {
        using var temp = new TempDirectory();
        var (manager, fake, _) = Create(temp);
        fake.Installed.Add(PrerequisiteCatalog.NetFrameworkId);
        var missing = manager.Missing(WithPrerequisites(PrerequisiteCatalog.NetFrameworkId, PrerequisiteCatalog.VcRedistId));
        Assert.Equal([PrerequisiteCatalog.VcRedistId], missing.Select(p => p.Id));
    }

    [Fact]
    public async Task InstallsVcRedistSilentlyAfterCheckingSignature()
    {
        using var temp = new TempDirectory();
        var (manager, fake, _) = Create(temp);
        await manager.InstallAsync(PrerequisiteCatalog.Known[PrerequisiteCatalog.VcRedistId], null, CancellationToken.None);
        Assert.Equal("/install /passive /norestart", Assert.Single(fake.Runs).Args);
        Assert.True(manager.IsInstalled(PrerequisiteCatalog.VcRedistId));
        // 実行したインストーラーは残さない
        Assert.False(File.Exists(fake.Runs[0].Path));
    }

    [Fact]
    public async Task RefusesUnsignedInstaller()
    {
        using var temp = new TempDirectory();
        var (manager, fake, _) = Create(temp);
        fake.SignatureOk = false;
        await Assert.ThrowsAsync<HubException>(() => manager.InstallAsync(PrerequisiteCatalog.Known[PrerequisiteCatalog.VcRedistId], null, CancellationToken.None));
        Assert.Empty(fake.Runs);
    }

    [Fact]
    public async Task ReportsFailedInstallation()
    {
        using var temp = new TempDirectory();
        var (manager, fake, _) = Create(temp);
        fake.ExitCode = 1603;
        await Assert.ThrowsAsync<HubException>(() => manager.InstallAsync(PrerequisiteCatalog.Known[PrerequisiteCatalog.VcRedistId], null, CancellationToken.None));

        // 終了コードが成功でも、入ったことを確かめられなければ失敗にする
        fake.ExitCode = 0;
        fake.InstallsOnRun = false;
        fake.Installed.Clear();
        await Assert.ThrowsAsync<HubException>(() => manager.InstallAsync(PrerequisiteCatalog.Known[PrerequisiteCatalog.VcRedistId], null, CancellationToken.None));
    }

    [Fact]
    public async Task InstallsXivLauncherFromOfficialRelease()
    {
        using var temp = new TempDirectory();
        var (manager, fake, _) = Create(temp);
        await manager.InstallAsync(PrerequisiteCatalog.Known[PrerequisiteCatalog.XivLauncherId], null, CancellationToken.None);
        Assert.Equal("", Assert.Single(fake.Runs).Args);
        Assert.True(manager.IsInstalled(PrerequisiteCatalog.XivLauncherId));
    }

    [Fact]
    public void BuiltInCatalogDeclaresPrerequisites()
    {
        using var temp = new TempDirectory();
        var catalog = new XIVToolHub.Core.Catalog.ToolCatalog(temp.CreatePaths());
        Assert.Contains(PrerequisiteCatalog.VcRedistId, catalog.Find("overlayplugin")!.Prerequisites);
        Assert.Contains(PrerequisiteCatalog.XivLauncherId, catalog.Find("glamaholic-jp")!.Prerequisites);
        Assert.NotEmpty(XIVToolHub.Core.Catalog.ToolCatalog.Validate(TestData.App() with { Prerequisites = ["unknown"] }));
    }
}
