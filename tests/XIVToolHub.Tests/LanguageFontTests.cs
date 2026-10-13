using System.Net;
using System.Text.Json;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Fonts;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Tests;

public class LanguageFontTests
{
    private static readonly byte[] TrueTypeHeader = [0x00, 0x01, 0x00, 0x00, 0x00, 0x0A];

    [Fact]
    public void Validate_RejectsUnknownOrDuplicateLanguages()
    {
        Assert.NotEmpty(ToolCatalog.Validate(TestData.Plugin() with { Languages = ["ja", "fr"] }));
        Assert.NotEmpty(ToolCatalog.Validate(TestData.Plugin() with { Languages = ["ja", "ja"] }));
        Assert.NotEmpty(ToolCatalog.Validate(TestData.Plugin() with { Languages = ["en"], SupportsFont = true }));
        Assert.Empty(ToolCatalog.Validate(TestData.Plugin() with { Languages = ["ja", "en", "ko", "zh"], SupportsFont = true }));
    }

    [Fact]
    public void BuiltInCatalog_LocalizedToolsSupportFourLanguages()
    {
        using var temp = new TempDirectory();
        var catalog = new ToolCatalog(temp.CreatePaths());

        foreach (var id in new[] { "glamaholic-jp", "triggevent-jp" })
        {
            var tool = catalog.Find(id)!;
            Assert.Equal(["ja", "en", "ko", "zh"], tool.Languages);
            Assert.True(tool.SupportsFont);
        }
        Assert.False(catalog.Find("act")!.SupportsLanguage);
    }

    [Fact]
    public void FontCatalog_HasThirtyFontsWithSafeUrls()
    {
        Assert.Equal(30, FontCatalog.Fonts.Count);
        Assert.Equal(FontCatalog.Fonts.Count, FontCatalog.Fonts.Select(f => f.Id).Distinct().Count());
        Assert.All(FontCatalog.Fonts.Where(f => f.CanDownload), f => Assert.StartsWith("https://", f.DownloadUrl, StringComparison.Ordinal));
        Assert.Equal("源暎ぽっぷる", FontCatalog.Find("genei-pople")!.Name);
        Assert.Null(FontCatalog.Find("missing"));
    }

    [Fact]
    public void Import_AcceptsFontFileAndRejectsFakeExtension()
    {
        using var temp = new TempDirectory();
        var store = new FontStore(temp.CreatePaths(), new HttpClient());
        var font = FontCatalog.Find("meomeo")!;

        var fake = temp.Combine("fake.ttf");
        File.WriteAllText(fake, "not a font");
        Assert.Throws<HubException>(() => store.Import(font, fake));
        Assert.Null(store.FindFontFile(font));

        var real = temp.Combine("meomeo.ttf");
        File.WriteAllBytes(real, TrueTypeHeader);
        var imported = store.Import(font, real);

        Assert.Equal(Path.Combine(store.FontDirectory("meomeo"), "meomeo.ttf"), imported);
        Assert.Equal(imported, store.ResolvePath("meomeo"));
    }

    [Fact]
    public void Import_ZipKeepsOnlyFontsAndPrefersConfiguredFile()
    {
        using var temp = new TempDirectory();
        var store = new FontStore(temp.CreatePaths(), new HttpClient());
        var font = FontCatalog.Find("genei-pople")!;
        var zip = temp.Combine("font.zip");
        File.WriteAllBytes(zip, FontZip(("GenEiPOPle/GenEiPOPlePw-Bk.ttf", true), ("GenEiPOPle/GenEiPOPle-Regular.ttf", true), ("readme.txt", false), ("evil.ttf", false)));

        var path = store.Import(font, zip);

        Assert.Equal("GenEiPOPlePw-Bk.ttf", Path.GetFileName(path));
        Assert.Equal(["GenEiPOPle-Regular.ttf", "GenEiPOPlePw-Bk.ttf"],
            Directory.GetFiles(store.FontDirectory(font.Id)).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Download_UsesCatalogUrlOnly()
    {
        using var temp = new TempDirectory();
        var handler = new UrlHandler();
        handler.Responses["https://raw.githubusercontent.com/google/fonts/main/ofl/hachimarupop/HachiMaruPop-Regular.ttf"] = TrueTypeHeader;
        var store = new FontStore(temp.CreatePaths(), new HttpClient(handler));

        var path = await store.DownloadAsync(FontCatalog.Find("hachi-maru-pop")!, null, CancellationToken.None);

        Assert.Equal("HachiMaruPop-Regular.ttf", Path.GetFileName(path));
        Assert.Single(handler.Requested);
        // 直接ダウンロードできないフォントは通信せずに断る
        await Assert.ThrowsAsync<HubException>(() => store.DownloadAsync(FontCatalog.Find("meomeo")!, null, CancellationToken.None));
        Assert.Single(handler.Requested);
        Assert.Empty(Directory.GetFiles(store.FontsDirectory));
    }

    [Fact]
    public async Task Install_WritesSettingsNextToEntryAndKeepsLanguageOnUpdate()
    {
        using var env = new Env();
        var tool = LocalizedApp();
        env.Publish("r1");

        await env.Installer.InstallAsync(tool, false, new ToolPreferences("ko", "hachi-maru-pop"), null, CancellationToken.None);

        var settingsPath = Path.Combine(env.Paths.ToolDirectory(tool.Id), "bin", ToolSettingsFile.FileName);
        Assert.Equal(("ko", null), ReadSettings(settingsPath));
        // 日本語以外ではフォントを記録しない
        Assert.Null(env.Installed.Find(tool.Id)!.FontId);

        env.Publish("r2");
        await env.Installer.InstallAsync(tool, false, null, CancellationToken.None);

        Assert.Equal("r2", env.Installed.Find(tool.Id)!.Version);
        Assert.Equal("ko", env.Installed.Find(tool.Id)!.Language);
        Assert.Equal(("ko", null), ReadSettings(settingsPath));
    }

    [Fact]
    public async Task Install_DefaultsToJapanese()
    {
        using var env = new Env();
        env.Publish("r1");

        await env.Installer.InstallAsync(LocalizedApp(), false, null, CancellationToken.None);

        Assert.Equal("ja", env.Installed.Find("loc-app")!.Language);
    }

    [Fact]
    public async Task ApplySettings_WritesFontPathOnlyForJapanese()
    {
        using var env = new Env();
        var tool = LocalizedApp();
        env.Publish("r1");
        await env.Installer.InstallAsync(tool, false, new ToolPreferences("en", null), null, CancellationToken.None);
        var fontFile = env.Temp.Combine("font.ttf");
        File.WriteAllBytes(fontFile, TrueTypeHeader);
        var imported = env.Fonts.Import(FontCatalog.Find("mochiy-pop")!, fontFile);
        var settingsPath = ToolSettingsFile.PathFor(env.Paths.ToolDirectory(tool.Id), tool);

        env.Installer.ApplySettings(tool, new ToolPreferences("ja", "mochiy-pop"));
        Assert.Equal(("ja", imported), ReadSettings(settingsPath));
        Assert.Equal("mochiy-pop", env.Installed.Find(tool.Id)!.FontId);

        env.Installer.ApplySettings(tool, new ToolPreferences("zh", "mochiy-pop"));
        Assert.Equal(("zh", null), ReadSettings(settingsPath));
        Assert.Null(env.Installed.Find(tool.Id)!.FontId);

        Assert.Throws<HubException>(() => env.Installer.ApplySettings(tool, new ToolPreferences("fr", null)));
        Assert.Throws<HubException>(() => env.Installer.ApplySettings(TestData.App(), new ToolPreferences("ja", null)));
    }

    [Fact]
    public void HubSettings_DefaultsToDarkAndPersistsTheme()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();

        var settings = new Core.Storage.HubSettingsStore(paths);
        Assert.Equal(HubThemes.Dark, settings.Theme);
        settings.SetTheme(HubThemes.Light);
        Assert.Equal(HubThemes.Light, new Core.Storage.HubSettingsStore(paths).Theme);
        Assert.Throws<HubException>(() => settings.SetTheme("pink"));

        // 壊れた設定ファイルでも既定のテーマで起動できる
        File.WriteAllText(paths.SettingsFile, "{ broken");
        Assert.Equal(HubThemes.Dark, new Core.Storage.HubSettingsStore(paths).Theme);
    }

    [Fact]
    public async Task Theme_IsWrittenOnInstallAndRefreshedForInstalledTools()
    {
        using var env = new Env();
        var tool = LocalizedApp();
        env.Publish("r1");
        await env.Installer.InstallAsync(tool, false, new ToolPreferences("ja", null), null, CancellationToken.None);
        var settingsPath = ToolSettingsFile.PathFor(env.Paths.ToolDirectory(tool.Id), tool);
        Assert.Equal(HubThemes.Dark, ReadTheme(settingsPath));

        env.Theme = HubThemes.Light;
        env.Installer.RefreshSettings(tool);

        Assert.Equal(HubThemes.Light, ReadTheme(settingsPath));
        Assert.Equal(("ja", null), ReadSettings(settingsPath));
        // 言語に対応しないツールには設定ファイルを作らない
        env.Installer.RefreshSettings(TestData.App());
    }

    private static string? ReadTheme(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("theme").GetString();
    }

    private static ToolDefinition LocalizedApp() => TestData.App("loc-app") with { Languages = ["ja", "en", "ko", "zh"], SupportsFont = true };

    private static (string? Language, string? FontPath) ReadSettings(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return (root.GetProperty("language").GetString(), root.GetProperty("fontPath").GetString());
    }

    private static byte[] FontZip(params (string Name, bool IsFont)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, isFont) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(isFont ? TrueTypeHeader : "text"u8.ToArray());
            }
        }
        return buffer.ToArray();
    }

    private sealed class UrlHandler : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Responses { get; } = [];

        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.AbsoluteUri;
            Requested.Add(url);
            return Task.FromResult(Responses.TryGetValue(url, out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class Env : IDisposable
    {
        private readonly FakeGitHubHandler _handler = new();
        private long _assetId;

        public Env()
        {
            Paths = Temp.CreatePaths();
            var clock = new ManualClock();
            var game = new FakeGameDetector();
            var github = new GitHubReleaseClient(new HttpClient(_handler) { BaseAddress = new Uri(GitHubReleaseClient.ApiBaseUrl) }, () => null);
            Installed = new InstalledStore(Paths);
            Fonts = new FontStore(Paths, new HttpClient());
            Installer = new ToolInstaller(Paths, github, Installed, new DalamudConfigEditor(Paths.DalamudConfigPath, game, clock),
                new ActConfigEditor(Paths.ToolDirectory(ToolCatalog.ActToolId), game, clock), game, clock, Fonts.ResolvePath, () => Theme);
        }

        public TempDirectory Temp { get; } = new();

        public string Theme { get; set; } = HubThemes.Dark;

        public Core.Storage.HubPaths Paths { get; }

        public InstalledStore Installed { get; }

        public FontStore Fonts { get; }

        public ToolInstaller Installer { get; }

        public void Publish(string tag)
        {
            var id = ++_assetId;
            _handler.Json("/repos/owner/app/releases/latest", TestData.ReleaseJson("owner", "app", tag, (id, "app-win.zip")));
            _handler.Bytes($"/repos/owner/app/releases/assets/{id}", TestData.Zip(("bin/app.exe", tag)));
        }

        public void Dispose() => Temp.Dispose();
    }
}
