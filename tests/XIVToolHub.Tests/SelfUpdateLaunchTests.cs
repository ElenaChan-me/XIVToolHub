using System.Net;
using System.Net.Sockets;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.SelfUpdate;

namespace XIVToolHub.Tests;

public class SelfUpdateTests
{
    private const string LatestPath = "/repos/ElenaChan-me/XIVToolHub/releases/latest";
    private const string AssetPath = "/repos/ElenaChan-me/XIVToolHub/releases/assets/1";

    private static GitHubReleaseClient Client(FakeGitHubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);

    private static FakeGitHubHandler Publish(string tag, params (string Name, string Content)[] files)
    {
        var handler = new FakeGitHubHandler();
        handler.Json(LatestPath, TestData.ReleaseJson("ElenaChan-me", "XIVToolHub", tag, (1, "XIVToolHub-win-x64.zip")));
        handler.Bytes(AssetPath, TestData.Zip(files));
        return handler;
    }

    [Theory]
    [InlineData("v0.4.1", "0.4.1.0")]
    [InlineData("0.5", "0.5.0.0")]
    [InlineData("v1.0.0-beta", "1.0.0.0")]
    public void ParsesTags(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), SelfUpdater.ParseVersion(tag));

    [Fact]
    public void RejectsUnreadableTags() => Assert.Null(SelfUpdater.ParseVersion("latest"));

    [Fact]
    public async Task ReportsOnlyNewerReleases()
    {
        using var temp = new TempDirectory();
        var handler = Publish("v0.5.0");
        var exe = temp.Combine(SelfUpdater.ExeName);

        Assert.NotNull(await new SelfUpdater(Client(handler), temp.CreatePaths(), exe, new Version(0, 4, 1, 0)).CheckAsync(CancellationToken.None));
        Assert.Null(await new SelfUpdater(Client(handler), temp.CreatePaths(), exe, new Version(0, 5, 0, 0)).CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReplacesRunningExeAndKeepsOldForCleanup()
    {
        using var temp = new TempDirectory();
        var exe = temp.Combine("app", SelfUpdater.ExeName);
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);
        File.WriteAllText(exe, "old");
        var updater = new SelfUpdater(Client(Publish("v0.5.0", (SelfUpdater.ExeName, "new"))), temp.CreatePaths(), exe, new Version(0, 4, 1, 0));

        var release = await updater.CheckAsync(CancellationToken.None);
        Assert.Equal(exe, await updater.InstallAsync(release!, null, CancellationToken.None));

        Assert.Equal("new", File.ReadAllText(exe));
        Assert.Equal("old", File.ReadAllText(exe + SelfUpdater.OldSuffix));
        SelfUpdater.CleanupOld(exe);
        Assert.False(File.Exists(exe + SelfUpdater.OldSuffix));
    }

    [Fact]
    public async Task KeepsCurrentExeWhenDownloadIsWrong()
    {
        using var temp = new TempDirectory();
        var exe = temp.Combine(SelfUpdater.ExeName);
        File.WriteAllText(exe, "old");
        var updater = new SelfUpdater(Client(Publish("v0.5.0", ("other.exe", "x"))), temp.CreatePaths(), exe, new Version(0, 4, 1, 0));

        var release = await updater.CheckAsync(CancellationToken.None);
        await Assert.ThrowsAsync<HubException>(() => updater.InstallAsync(release!, null, CancellationToken.None));
        Assert.Equal("old", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + SelfUpdater.OldSuffix));
    }

    [Fact]
    public void OnlySingleFileBuildsUpdate()
    {
        using var temp = new TempDirectory();
        var exe = temp.Combine(SelfUpdater.ExeName);
        Assert.True(SelfUpdater.CanUpdate(exe));
        // 開発中のビルドは dll が並ぶので、exe 1つで上書きしない
        File.WriteAllText(temp.Combine("XIVToolHub.dll"), "");
        Assert.False(SelfUpdater.CanUpdate(exe));
        Assert.False(SelfUpdater.CanUpdate(temp.Combine("dotnet.exe")));
    }
}

public class LaunchSequenceTests
{
    private static readonly ToolDefinition Act = TestData.App("act");
    private static readonly ToolDefinition Triggevent = TestData.App("triggevent-jp");

    private static string WriteWsConfig(TempDirectory temp, bool running, int port = 10600)
    {
        var actDir = temp.Combine("act");
        var path = Path.Combine(actDir, ActConfigEditor.ConfigFolderName, OverlayWebSocketSettings.ConfigFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var flag = running ? "true" : "false";
        File.WriteAllText(path, "{\"WSServerIP\":\"127.0.0.1\",\"WSServerPort\":" + port + ",\"WSServerRunning\":" + flag + "}");
        return actDir;
    }

    [Fact]
    public async Task StartsActThenTriggeventAfterWebSocketIsReady()
    {
        using var temp = new TempDirectory();
        var actDir = WriteWsConfig(temp, running: true);
        var launched = new List<string>();
        var probed = new List<int>();
        var sequence = new LaunchSequence(t => launched.Add(t.Id), new FakeGameDetector(), new FakeGameDetector(), new ManualClock(),
            (port, _) =>
            {
                probed.Add(port);
                return Task.FromResult(true);
            });

        var messages = await sequence.RunAsync(Act, actDir, Triggevent, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Equal(["act", "triggevent-jp"], launched);
        Assert.Equal([10600], probed);
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task SkipsRunningToolsAndWarnsWhenServerIsStopped()
    {
        using var temp = new TempDirectory();
        var actDir = WriteWsConfig(temp, running: false);
        var launched = new List<string>();
        // サーバーが止まっているときは待たない
        var sequence = new LaunchSequence(t => launched.Add(t.Id), new FakeGameDetector { Running = true }, new FakeGameDetector { Running = true },
            new ManualClock(), (_, _) => throw new InvalidOperationException("probe should not be called"));

        var messages = await sequence.RunAsync(Act, actDir, Triggevent, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.Empty(launched);
        Assert.Contains(messages, m => m.Contains("開始されていません"));
    }

    [Fact]
    public async Task LaunchesTriggeventEvenWhenWebSocketNeverAnswers()
    {
        using var temp = new TempDirectory();
        var actDir = WriteWsConfig(temp, running: true);
        var launched = new List<string>();
        var sequence = new LaunchSequence(t => launched.Add(t.Id), new FakeGameDetector(), new FakeGameDetector(), new ManualClock(),
            (_, _) => Task.FromResult(false));

        var messages = await sequence.RunAsync(Act, actDir, Triggevent, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(["act", "triggevent-jp"], launched);
        Assert.Contains(messages, m => m.Contains("つながりませんでした"));
    }

    [Fact]
    public void ReadsDefaultsWithoutConfig()
    {
        using var temp = new TempDirectory();
        Assert.Equal(new OverlayWebSocketSettings(OverlayWebSocketSettings.DefaultPort, false), OverlayWebSocketSettings.Read(temp.Combine("act")));
    }

    [Fact]
    public async Task ProbesLocalPorts()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.True(await LaunchSequence.IsListeningAsync(port, CancellationToken.None));
        }
        finally
        {
            listener.Stop();
        }
    }
}
