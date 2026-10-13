using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace XIVToolHub.Core.Storage;

internal static class JsonFiles
{
    // 保存ファイルとして十分に大きく、誤って巨大なファイルを開いたときは止められる値
    private const long MaxFileBytes = 8 * 1024 * 1024;

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            RespectNullableAnnotations = true,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    public static T? Read<T>(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return default;
        }
        if (info.Length > MaxFileBytes)
        {
            throw new HubException($"ファイルが大きすぎます: {path}");
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            throw new HubException($"ファイルの形式が不正です: {path}", ex);
        }
    }

    public static void Write<T>(string path, T value) =>
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(value, Options));
}
