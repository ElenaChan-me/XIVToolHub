using System.Text.Json;
using System.Text.Json.Serialization;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Install;

/// <summary>
/// ツールの表示言語と、日本語表示で使う追加フォント。
/// </summary>
public sealed record ToolPreferences(string Language, string? FontId)
{
    /// <summary>まだ選ばれていない場合の既定値。日本語化したツールなので日本語にする。</summary>
    public static ToolPreferences DefaultFor(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var language = tool.Languages.Contains(ToolLanguages.Japanese) ? ToolLanguages.Japanese : tool.Languages.FirstOrDefault() ?? ToolLanguages.English;
        return new ToolPreferences(language, null);
    }
}

/// <summary>
/// ツールに表示言語・フォント・テーマを伝える設定ファイル。ツールの起動ファイル・DLL と同じフォルダに置く。
/// </summary>
public static class ToolSettingsFile
{
    public const string FileName = "xivtoolhub-settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    public static string PathFor(string installDirectory, ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var entry = System.IO.Path.Combine(installDirectory, tool.EntryFile);
        return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(entry)!, FileName);
    }

    /// <param name="voicePacks">インストール済みのボイスパックのフォルダ。Triggevent Plus が一覧に出す。</param>
    public static void Write(string installDirectory, ToolDefinition tool, ToolPreferences preferences, string? fontPath, string theme,
        IReadOnlyList<string>? voicePacks = null)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (!ToolLanguages.IsSupported(preferences.Language) || !tool.Languages.Contains(preferences.Language))
        {
            throw new HubException($"{tool.Name} はこの言語に対応していません。");
        }
        // フォントは日本語表示のときだけ使う
        var content = new SettingsContent(preferences.Language, preferences.Language == ToolLanguages.Japanese ? fontPath : null, HubThemes.Normalize(theme),
            voicePacks ?? []);
        AtomicFile.WriteAllText(PathFor(installDirectory, tool), JsonSerializer.Serialize(content, Options));
    }

    private sealed record SettingsContent(
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("fontPath")] string? FontPath,
        [property: JsonPropertyName("theme")] string Theme,
        [property: JsonPropertyName("voicePacks")] IReadOnlyList<string> VoicePacks);
}
