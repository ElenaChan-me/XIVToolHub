using System.Text.Json.Nodes;
using XIVToolHub.Core;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Install;

namespace XIVToolHub.Tests;

public class OverlayPluginConfigTests
{
    [Fact]
    public void CreatesMinimalConfigBeforeFirstLaunch()
    {
        using var temp = new TempDirectory();
        var act = temp.Combine("act");
        Assert.True(OverlayPluginConfig.EnableWebSocket(act, new ManualClock()));
        Assert.Equal(new OverlayWebSocketSettings(OverlayWebSocketSettings.DefaultPort, true), OverlayWebSocketSettings.Read(act));
        var json = JsonNode.Parse(File.ReadAllText(OverlayPluginConfig.PathFor(act)))!;
        Assert.Equal(OverlayPluginConfig.LocalAddress, json["WSServerIP"]!.GetValue<string>());
        // 2回目は変える所が無い
        Assert.False(OverlayPluginConfig.EnableWebSocket(act, new ManualClock()));
    }

    [Fact]
    public void KeepsOtherSettingsAndBacksUp()
    {
        using var temp = new TempDirectory();
        var act = temp.Combine("act");
        var path = OverlayPluginConfig.PathFor(act);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"Overlays\":[{\"Name\":\"x\"}],\"WSServerIP\":\"192.168.0.2\",\"WSServerPort\":12000,\"WSServerSSL\":true,\"WSServerRunning\":false}");

        Assert.True(OverlayPluginConfig.EnableWebSocket(act, new ManualClock()));

        var json = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.True(json["WSServerRunning"]!.GetValue<bool>());
        Assert.Equal("192.168.0.2", json["WSServerIP"]!.GetValue<string>());
        Assert.Equal(12000, json["WSServerPort"]!.GetValue<int>());
        Assert.True(json["WSServerSSL"]!.GetValue<bool>());
        Assert.Equal("x", json["Overlays"]![0]!["Name"]!.GetValue<string>());
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.bak"));
    }

    [Fact]
    public void RejectsBrokenConfig()
    {
        using var temp = new TempDirectory();
        var act = temp.Combine("act");
        var path = OverlayPluginConfig.PathFor(act);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ broken");
        Assert.Throws<HubException>(() => OverlayPluginConfig.EnableWebSocket(act, new ManualClock()));
        Assert.Equal("{ broken", File.ReadAllText(path));
    }
}
