using System.Text.Json.Nodes;
using XIVToolHub.Core;
using XIVToolHub.Core.Dalamud;

namespace XIVToolHub.Tests;

public class DalamudConfigTests
{
    [Fact]
    public void Register_AddsTypedEntryEnablesDevModeAndKeepsOtherSettings()
    {
        using var temp = new TempDirectory();
        var (editor, path) = Create(temp, TestData.DalamudConfig(TestData.EmptyLocations, devMode: false));
        var dll = temp.Combine("tools", "p", "Plugin.dll");

        var result = editor.Register(dll);

        Assert.True(result.Changed);
        Assert.True(result.DevModeEnabled);
        Assert.True(File.Exists(result.BackupPath));
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal("ja", root["LanguageOverride"]!.GetValue<string>());
        Assert.True(root["DevMode"]!.GetValue<bool>());
        var entry = root["DevPluginLoadLocations"]!["$values"]!.AsArray().Single()!.AsObject();
        Assert.Equal("Dalamud.Configuration.DevPluginLocationSettings, Dalamud", entry["$type"]!.GetValue<string>());
        Assert.Equal(dll, entry["Path"]!.GetValue<string>());
        Assert.True(entry["IsEnabled"]!.GetValue<bool>());
        // Newtonsoft の型名は先頭にないと解釈されない
        Assert.Equal("$type", entry.First().Key);
        Assert.NotNull(root["ThirdRepoList"]);
    }

    [Fact]
    public void Register_IsIdempotentAndReenablesDisabledEntry()
    {
        using var temp = new TempDirectory();
        var dll = temp.Combine("Plugin.dll");
        var locations = $$"""{ "$type": "x", "$values": [ { "$type": "Dalamud.Configuration.DevPluginLocationSettings, Dalamud", "Path": {{System.Text.Json.JsonSerializer.Serialize(dll.ToUpperInvariant())}}, "IsEnabled": false } ] }""";
        var (editor, path) = Create(temp, TestData.DalamudConfig(locations, devMode: true));

        Assert.True(editor.Register(dll).Changed);
        var second = editor.Register(dll);

        Assert.False(second.Changed);
        Assert.Null(second.BackupPath);
        var values = JsonNode.Parse(File.ReadAllText(path))!["DevPluginLoadLocations"]!["$values"]!.AsArray();
        Assert.Single(values);
        Assert.True(values[0]!["IsEnabled"]!.GetValue<bool>());
    }

    [Fact]
    public void Register_CreatesListWhenMissingAndSupportsPlainArray()
    {
        using var temp = new TempDirectory();
        var (editor, path) = Create(temp, """{ "$type": "Dalamud.Configuration.Internal.DalamudConfiguration, Dalamud" }""");
        editor.Register(temp.Combine("a.dll"));
        Assert.Single(JsonNode.Parse(File.ReadAllText(path))!["DevPluginLoadLocations"]!["$values"]!.AsArray());

        using var temp2 = new TempDirectory();
        var (editor2, path2) = Create(temp2, TestData.DalamudConfig("[]", devMode: null));
        editor2.Register(temp2.Combine("b.dll"));
        Assert.Single(JsonNode.Parse(File.ReadAllText(path2))!["DevPluginLoadLocations"]!.AsArray());
    }

    [Fact]
    public void Unregister_RemovesOnlyMatchingEntryAndKeepsDevMode()
    {
        using var temp = new TempDirectory();
        var (editor, path) = Create(temp, TestData.DalamudConfig(TestData.EmptyLocations, devMode: true));
        var keep = temp.Combine("keep.dll");
        var remove = temp.Combine("remove.dll");
        editor.Register(keep);
        editor.Register(remove);

        Assert.True(editor.Unregister(remove));
        Assert.False(editor.Unregister(remove));

        Assert.Equal([keep], editor.GetRegisteredPaths());
        Assert.True(JsonNode.Parse(File.ReadAllText(path))!["DevMode"]!.GetValue<bool>());
    }

    [Fact]
    public void Edit_IsRefusedWhileGameRunningOrConfigMissing()
    {
        using var temp = new TempDirectory();
        var (editor, path) = Create(temp, TestData.DalamudConfig(TestData.EmptyLocations, devMode: true), gameRunning: true);
        var before = File.ReadAllText(path);

        Assert.Throws<HubException>(() => editor.Register(temp.Combine("a.dll")));
        Assert.Equal(before, File.ReadAllText(path));

        var missing = new DalamudConfigEditor(temp.Combine("none.json"), new FakeGameDetector(), new ManualClock());
        Assert.Throws<HubException>(() => missing.Register(temp.Combine("a.dll")));
        Assert.Empty(missing.GetRegisteredPaths());
    }

    [Fact]
    public void BrokenConfig_IsNotOverwritten()
    {
        using var temp = new TempDirectory();
        var (editor, path) = Create(temp, "{ broken");

        Assert.Throws<HubException>(() => editor.Register(temp.Combine("a.dll")));
        Assert.Equal("{ broken", File.ReadAllText(path));
    }

    private static (DalamudConfigEditor Editor, string Path) Create(TempDirectory temp, string content, bool gameRunning = false)
    {
        var path = temp.Combine("XIVLauncher", "dalamudConfig.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        var clock = new ManualClock();
        return (new DalamudConfigEditor(path, new FakeGameDetector { Running = gameRunning }, clock), path);
    }
}
