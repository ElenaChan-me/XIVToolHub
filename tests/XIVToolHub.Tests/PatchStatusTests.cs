using System.Text.Json;
using XIVToolHub.Core.Compatibility;
using XIVToolHub.Core.GitHub;

namespace XIVToolHub.Tests;

public class PatchStatusTests
{
    private const string DalamudPath = "/Dalamud/Release/VersionInfo?track=release";
    private const string ActPath = "/repos/ravahn/FFXIV_ACT_Plugin/releases/latest";

    private static string WriteGame(TempDirectory temp, string? version)
    {
        var game = temp.Combine("FINAL FANTASY XIV Online");
        Directory.CreateDirectory(Path.Combine(game, "game"));
        if (version is not null)
        {
            File.WriteAllText(Path.Combine(game, "game", PatchStatusChecker.GameVersionFile), version);
        }
        var config = temp.Combine("launcherConfigV3.json");
        File.WriteAllText(config, JsonSerializer.Serialize(new { GamePath = game }));
        return config;
    }

    private static PatchStatusChecker CreateChecker(string config, FakeGitHubHandler handler) =>
        new(config, new HttpClient(handler), new GitHubReleaseClient(new HttpClient(handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null));

    [Fact]
    public async Task ReportsDalamudSupportedAndActPluginNotYetUpdated()
    {
        using var temp = new TempDirectory();
        var config = WriteGame(temp, "2026.10.06.0000.0000");
        var handler = new FakeGitHubHandler();
        handler.Json(DalamudPath, """{"supportedGameVer":"2026.10.06.0000.0000"}""");
        // ReleaseJson の公開日は 2026-10-01 で、ゲームのビルド日より前
        handler.Json(ActPath, TestData.ReleaseJson("ravahn", "FFXIV_ACT_Plugin", "3.0.3.3", (1, "FFXIV_ACT_Plugin.zip")));

        var status = await CreateChecker(config, handler).CheckAsync(CancellationToken.None);

        Assert.Equal("2026.10.06.0000.0000", status.GameVersion);
        Assert.Equal(SupportState.Supported, status.Dalamud);
        Assert.Equal(SupportState.NotYetSupported, status.ActPlugin);
        Assert.Empty(status.Problems);
        Assert.True(PatchStatusChecker.NeedsAttention(status));
        Assert.Contains("未対応の可能性", PatchStatusChecker.Describe(status));
    }

    [Fact]
    public async Task UnknownWhenGameOrServersAreMissing()
    {
        using var temp = new TempDirectory();
        var config = WriteGame(temp, null);
        var status = await CreateChecker(config, new FakeGitHubHandler()).CheckAsync(CancellationToken.None);

        Assert.Null(status.GameVersion);
        Assert.Equal(SupportState.Unknown, status.Dalamud);
        Assert.Equal(SupportState.Unknown, status.ActPlugin);
        Assert.Equal(3, status.Problems.Count);
        Assert.False(PatchStatusChecker.NeedsAttention(status));
    }

    [Theory]
    [InlineData("2026.10.06.0000.0000", "2026.10.06.0000.0000", SupportState.Supported)]
    [InlineData("2026.10.20.0000.0001", "2026.10.06.0000.0000", SupportState.NotYetSupported)]
    [InlineData("2026.10.06.0000.0000", "2026.10.20.0000.0000", SupportState.GameUpdateRequired)]
    [InlineData(null, "2026.10.20.0000.0000", SupportState.Unknown)]
    public void ComparesDalamudVersions(string? game, string supported, SupportState expected) =>
        Assert.Equal(expected, PatchStatusChecker.CompareDalamud(game, supported));

    [Fact]
    public void RejectsMalformedVersions()
    {
        Assert.Equal(new DateOnly(2026, 10, 6), PatchStatusChecker.ParseVersionDate("2026.10.06.0000.0000"));
        Assert.Null(PatchStatusChecker.ParseVersionDate("2026.13.06.0000.0000"));
        Assert.Null(PatchStatusChecker.ParseVersionDate("../../etc/passwd/xx"));
        Assert.Null(PatchStatusChecker.ParseVersionDate(""));
    }

    [Fact]
    public void ActPluginReleasedAfterBuildIsSupported() =>
        Assert.Equal(SupportState.Supported,
            PatchStatusChecker.CompareActPlugin("2026.10.06.0000.0000", new DateTimeOffset(2026, 10, 13, 20, 52, 14, TimeSpan.Zero)));
}
