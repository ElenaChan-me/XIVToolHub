using XIVToolHub.Core;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Tests;

public class CatalogTests
{
    [Fact]
    public void BuiltInCatalog_IsValidAndContainsGlamaholicJp()
    {
        using var temp = new TempDirectory();
        var catalog = new ToolCatalog(temp.CreatePaths());

        Assert.NotEmpty(catalog.Tools);
        Assert.All(catalog.Tools, t => Assert.Empty(ToolCatalog.Validate(t)));
        Assert.All(catalog.Tools, t => Assert.True(t.IsBuiltIn));
        var glamaholic = Assert.Single(catalog.Tools, t => t.Id == "glamaholic-jp");
        Assert.Equal(ToolKind.DalamudPlugin, glamaholic.Kind);
    }

    [Fact]
    public void AddAndRemoveUserTool_PersistsAcrossInstances()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        var catalog = new ToolCatalog(paths);

        catalog.AddUserTool(TestData.App("my-tool"));
        Assert.False(new ToolCatalog(paths).Find("my-tool")!.IsBuiltIn);

        catalog.RemoveUserTool("my-tool");
        Assert.Null(new ToolCatalog(paths).Find("my-tool"));
    }

    [Fact]
    public void AddUserTool_RejectsDuplicateIdAndBuiltInRemoval()
    {
        using var temp = new TempDirectory();
        var catalog = new ToolCatalog(temp.CreatePaths());

        Assert.Throws<HubException>(() => catalog.AddUserTool(TestData.Plugin("glamaholic-jp")));
        Assert.Throws<HubException>(() => catalog.RemoveUserTool("glamaholic-jp"));
    }

    public static TheoryData<string, Func<ToolDefinition, ToolDefinition>> InvalidDefinitions => new()
    {
        { "ID に大文字", t => t with { Id = "Bad" } },
        { "ID が1文字", t => t with { Id = "a" } },
        { "名前なし", t => t with { Name = " " } },
        { "オーナー不正", t => t with { Source = t.Source with { Owner = "own/er" } } },
        { "リポジトリが ..", t => t with { Source = t.Source with { Repo = ".." } } },
        { "アセットにパス", t => t with { Source = t.Source with { AssetPattern = "../x.zip" } } },
        { "起動ファイルが絶対パス", t => t with { EntryFile = @"C:\Windows\notepad.exe" } },
        { "起動ファイルが親を指す", t => t with { EntryFile = "../evil.exe" } },
        { "アプリなのに DLL", t => t with { EntryFile = "app.dll" } },
        { "プラグインなのに exe", t => t with { Kind = ToolKind.DalamudPlugin, EntryFile = "app.exe" } },
    };

    [Theory]
    [MemberData(nameof(InvalidDefinitions))]
    public void Validate_RejectsInvalidDefinitions(string label, Func<ToolDefinition, ToolDefinition> corrupt)
    {
        Assert.NotEmpty(label);
        Assert.NotEmpty(ToolCatalog.Validate(corrupt(TestData.App())));
    }

    [Fact]
    public void BrokenUserCatalogEntries_AreIgnored()
    {
        using var temp = new TempDirectory();
        var paths = temp.CreatePaths();
        Directory.CreateDirectory(paths.SettingsDirectory);
        File.WriteAllText(paths.UserCatalogFile, """
            [
              {"id":"ok-tool","name":"OK","kind":"application","source":{"owner":"a","repo":"b","assetPattern":"x.zip"},"entryFile":"x.exe"},
              {"id":"bad-tool","name":"NG","kind":"application","source":{"owner":"a","repo":"b","assetPattern":"x.zip"},"entryFile":"../x.exe"}
            ]
            """);

        var catalog = new ToolCatalog(paths);

        Assert.NotNull(catalog.Find("ok-tool"));
        Assert.Null(catalog.Find("bad-tool"));
    }
}
