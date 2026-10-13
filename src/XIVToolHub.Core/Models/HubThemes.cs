namespace XIVToolHub.Core.Models;

public sealed record ThemeOption(string Code, string DisplayName);

/// <summary>
/// XIVToolHub と、ハブからインストールしたツールで共通に使う配色。
/// </summary>
public static class HubThemes
{
    /// <summary>黒の背景に赤のアクセント。</summary>
    public const string Dark = "dark";

    /// <summary>白の背景に水色のアクセント。</summary>
    public const string Light = "light";

    public const string Default = Dark;

    public static IReadOnlyList<ThemeOption> All { get; } =
    [
        new(Dark, "黒・赤"),
        new(Light, "白・水色"),
    ];

    public static bool IsSupported(string? code) => All.Any(t => t.Code == code);

    public static string Normalize(string? code) => IsSupported(code) ? code! : Default;
}
