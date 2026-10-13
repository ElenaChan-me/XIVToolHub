using System.Reflection;
using System.Text.Json;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Fonts;

/// <summary>
/// 日本語表示で選べる追加フォント。フォントは同梱せず、公式の配布元から取得するか利用者に取り込んでもらう。
/// </summary>
public sealed record FontDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Author { get; init; }

    public required string OfficialUrl { get; init; }

    public required string License { get; init; }

    public required string Coverage { get; init; }

    /// <summary>公式に直接ダウンロードできる URL。ログインが必要な配布元などは null で、手動で取り込む。</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>ZIP に複数のフォントが入っている場合に優先して使うファイル名。</summary>
    public string? PreferredFile { get; init; }

    /// <summary>規約上の注意など、選ぶ前に知らせておくこと。</summary>
    public string? Warning { get; init; }

    public bool CanDownload => DownloadUrl is not null;
}

public static class FontCatalog
{
    private const string ResourceName = "XIVToolHub.Core.builtin-fonts.json";

    public static IReadOnlyList<FontDefinition> Fonts { get; } = Load();

    public static FontDefinition? Find(string? id) => id is null ? null : Fonts.FirstOrDefault(f => f.Id == id);

    private static List<FontDefinition> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("フォントカタログが見つかりません。");
        return JsonSerializer.Deserialize<List<FontDefinition>>(stream, JsonFiles.Options) ?? [];
    }
}
