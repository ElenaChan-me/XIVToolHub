using System.IO.Compression;
using System.Net;
using System.Text;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "XIVToolHubTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public HubPaths CreatePaths() => new()
    {
        DataDirectory = Combine("local"),
        SettingsDirectory = Combine("roaming"),
        DalamudConfigPath = Combine("XIVLauncher", "dalamudConfig.json"),
    };

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(9));

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.CreateCustomTimeZone("test", Now.Offset, "test", "test");

    public void Advance() => Now = Now.AddSeconds(1);
}

internal sealed class FakeGameDetector : IGameProcessDetector
{
    public bool Running { get; set; }

    public bool IsGameRunning() => Running;
}

/// <summary>GitHub API の代わりに決まった応答を返す。</summary>
internal sealed class FakeGitHubHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = [];

    public List<HttpRequestMessage> Requests { get; } = [];

    public void Json(string path, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes[path] = _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public void Bytes(string path, byte[] content) =>
        _routes[path] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var key = request.RequestUri!.PathAndQuery;
        return Task.FromResult(_routes.TryGetValue(key, out var route)
            ? route(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal static class TestData
{
    public static ToolDefinition Plugin(string id = "test-plugin") => new()
    {
        Id = id,
        Name = "テストプラグイン",
        Kind = ToolKind.DalamudPlugin,
        Source = new GitHubSource { Owner = "owner", Repo = "plugin", AssetPattern = "latest.zip" },
        EntryFile = "TestPlugin.dll",
    };

    public static ToolDefinition App(string id = "test-app") => new()
    {
        Id = id,
        Name = "テストアプリ",
        Kind = ToolKind.Application,
        Source = new GitHubSource { Owner = "owner", Repo = "app", AssetPattern = "app-*.zip" },
        EntryFile = "bin/app.exe",
    };

    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }
        return buffer.ToArray();
    }

    public static string ReleaseJson(string owner, string repo, string tag, params (long Id, string Name)[] assets)
    {
        var items = string.Join(",", assets.Select(a =>
            $$"""{"id":{{a.Id}},"name":"{{a.Name}}","size":100,"url":"https://api.github.com/repos/{{owner}}/{{repo}}/releases/assets/{{a.Id}}"}"""));
        return $$"""{"tag_name":"{{tag}}","name":"{{tag}}","draft":false,"prerelease":false,"published_at":"2026-10-01T00:00:00Z","assets":[{{items}}]}""";
    }

    public static string DalamudConfig(string devPluginsJson, bool? devMode) =>
        $$"""
        {
          "$type": "Dalamud.Configuration.Internal.DalamudConfiguration, Dalamud",
          "LanguageOverride": "ja",
          {{(devMode is null ? "" : $"\"DevMode\": {devMode.Value.ToString().ToLowerInvariant()},")}}
          "DevPluginLoadLocations": {{devPluginsJson}},
          "ThirdRepoList": { "$type": "System.Collections.Generic.List`1[[Dalamud.Configuration.ThirdPartyRepoSettings, Dalamud]], System.Private.CoreLib", "$values": [] }
        }
        """;

    public const string EmptyLocations =
        """{ "$type": "System.Collections.Generic.List`1[[Dalamud.Configuration.DevPluginLocationSettings, Dalamud]], System.Private.CoreLib", "$values": [] }""";
}
