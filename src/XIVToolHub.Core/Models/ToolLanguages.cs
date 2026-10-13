namespace XIVToolHub.Core.Models;

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// 日本語化したツールで選べる表示言語。中国語は FF14 中国版に合わせて簡体字。
/// </summary>
public static class ToolLanguages
{
    public const string English = "en";
    public const string Japanese = "ja";
    public const string Korean = "ko";
    public const string ChineseSimplified = "zh";

    public static IReadOnlyList<LanguageOption> All { get; } =
    [
        new(Japanese, "日本語"),
        new(English, "English（英語）"),
        new(Korean, "한국어（韓国語）"),
        new(ChineseSimplified, "简体中文（中国語）"),
    ];

    public static bool IsSupported(string? code) => All.Any(l => l.Code == code);

    public static string DisplayName(string? code) => All.FirstOrDefault(l => l.Code == code)?.DisplayName ?? "-";
}
