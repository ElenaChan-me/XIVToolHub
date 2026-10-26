using System.IO.Compression;
using System.Net;
using System.Text;
using XIVToolHub.Core;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.SelfUpdate;

namespace XIVToolHub.Tests;

/// <summary>開発者以外の PC で起きやすい状況のテスト。</summary>
public class PortabilityTests
{
    [Fact]
    public void ExtractsZipWithShiftJisNames()
    {
        using var temp = new TempDirectory();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var zip = temp.Combine("sjis.zip");
        // 日本語の Windows のエクスプローラーで作った ZIP と同じく、UTF-8 の印を付けずに Shift_JIS で名前を書く
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create, Encoding.GetEncoding(932)))
        {
            using var writer = new StreamWriter(archive.CreateEntry("フォント/説明.txt").Open());
            writer.Write("x");
        }

        SafeZipExtractor.Extract(zip, temp.Combine("out"));

        Assert.True(File.Exists(temp.Combine("out", "フォント", "説明.txt")));
    }

    [Fact]
    public void DuplicateNamesInZipFailWithoutCrashing()
    {
        using var temp = new TempDirectory();
        var zip = temp.Combine("dup.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("a.txt");
            archive.CreateEntry("a.txt");
        }
        Assert.Throws<HubException>(() => SafeZipExtractor.Extract(zip, temp.Combine("out")));
    }

    [Fact]
    public async Task ReusesReleaseLookupsForTheSameRepository()
    {
        var handler = new FakeGitHubHandler();
        handler.Json("/repos/owner/app/releases/latest", TestData.ReleaseJson("owner", "app", "r1", (1, "app-win.zip")));
        var client = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var source = TestData.App().Source;

        await client.GetLatestReleaseCachedAsync(source, CancellationToken.None);
        await client.GetLatestReleaseCachedAsync(source, CancellationToken.None);
        Assert.Single(handler.Requests);

        client.ClearReleaseCache();
        await client.GetLatestReleaseCachedAsync(source, CancellationToken.None);
        Assert.Equal(2, handler.Requests.Count);
        // インストールの直前は使い回さない
        await client.GetLatestReleaseAsync(source, CancellationToken.None);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task FailedLookupsAreRetried()
    {
        var handler = new FakeGitHubHandler();
        var client = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var source = TestData.App().Source;
        var error = await Assert.ThrowsAsync<HubException>(() => client.GetLatestReleaseCachedAsync(source, CancellationToken.None));
        Assert.Contains("アクセス権", error.Message, StringComparison.Ordinal);

        // 失敗した結果は使い回さない
        handler.Json("/repos/owner/app/releases/latest", TestData.ReleaseJson("owner", "app", "r1", (1, "app-win.zip")));
        Assert.Equal("r1", (await client.GetLatestReleaseCachedAsync(source, CancellationToken.None)).TagName);
    }

    [Fact]
    public async Task DownloadFailureBecomesHubException()
    {
        using var temp = new TempDirectory();
        var handler = new FakeGitHubHandler();
        handler.Route("/repos/owner/app/releases/assets/1", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FailingStream()) });
        var client = new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
        var asset = new ReleaseAsset(1, "app-win.zip", 100, "https://api.github.com/repos/owner/app/releases/assets/1");

        await Assert.ThrowsAsync<HubException>(() => client.DownloadAssetAsync(TestData.App().Source, asset, temp.Combine("a.zip"), null, CancellationToken.None));
        Assert.False(File.Exists(temp.Combine("a.zip.part")));
    }

    [Theory]
    [InlineData("XIVToolHub.exe", true)]
    [InlineData("XIVToolHub (1).exe", true)]
    [InlineData("dotnet.exe", false)]
    [InlineData("XIVToolHub.dll", false)]
    public void SelfUpdateAcceptsRenamedDownloads(string name, bool expected)
    {
        using var temp = new TempDirectory();
        Assert.Equal(expected, SelfUpdater.CanUpdate(temp.Combine(name)));
    }

    [Fact]
    public async Task RefusesToStartWhenAnotherActIsRunning()
    {
        using var temp = new TempDirectory();
        var launched = new List<string>();
        var sequence = new LaunchSequence(t => launched.Add(t.Id), new FakeGameDetector(), new FakeGameDetector(), new ManualClock(),
            (_, _) => Task.FromResult(true), anyAct: new FakeGameDetector { Running = true });

        var error = await Assert.ThrowsAsync<HubException>(() =>
            sequence.RunAsync(TestData.App("act"), temp.Combine("act"), TestData.App("triggevent-jp"), TimeSpan.Zero, CancellationToken.None));

        Assert.Contains("別の ACT", error.Message, StringComparison.Ordinal);
        Assert.Empty(launched);
    }

    private sealed class FailingStream : MemoryStream
    {
        public FailingStream() : base(new byte[10])
        {
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("connection reset");

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection reset");
    }
}
