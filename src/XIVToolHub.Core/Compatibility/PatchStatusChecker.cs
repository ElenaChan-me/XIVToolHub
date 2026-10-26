using System.Globalization;
using System.Text.Json;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Core.Compatibility;

/// <summary>今のゲームへの対応状況。</summary>
public enum SupportState
{
    Unknown,
    Supported,
    /// <summary>ゲームが新しく、ツールの対応がまだ。</summary>
    NotYetSupported,
    /// <summary>ツールのほうが新しく、ゲームの更新を待っている。</summary>
    GameUpdateRequired,
}

public sealed record PatchStatus(
    string? GameVersion,
    string? DalamudSupportedVersion,
    SupportState Dalamud,
    string? ActPluginVersion,
    DateTimeOffset? ActPluginPublishedAt,
    SupportState ActPlugin,
    IReadOnlyList<string> Problems);

/// <summary>
/// パッチ直後に Dalamud と FFXIV_ACT_Plugin が今のゲームに対応しているかを調べる。
/// <para>
/// Dalamud は XIVLauncher と同じく、配布サーバーが示す対応ゲームバージョンとインストール済みのゲームのバージョンを比べる。
/// FFXIV_ACT_Plugin は対応バージョンを公開していないため、ゲームのビルド日より後にリリースされているかで判断する。
/// </para>
/// </summary>
public sealed class PatchStatusChecker
{
    public const string DalamudVersionInfoUrl = "https://kamori.goats.dev/Dalamud/Release/VersionInfo?track=release";
    public const string GameVersionFile = "ffxivgame.ver";

    public static readonly GitHubSource ActPluginSource = new()
    {
        Owner = "ravahn",
        Repo = "FFXIV_ACT_Plugin",
        AssetPattern = "*.zip",
    };

    // 設定ファイルが壊れていても大きなファイルを読み込まないようにする
    private const long MaxLauncherConfigBytes = 4L * 1024 * 1024;
    private const int MaxVersionInfoBytes = 1024 * 1024;

    private readonly string _launcherConfigPath;
    private readonly HttpClient _http;
    private readonly GitHubReleaseClient _github;

    /// <param name="http">GitHub 以外へ問い合わせるクライアント。</param>
    public PatchStatusChecker(string launcherConfigPath, HttpClient http, GitHubReleaseClient github)
    {
        _launcherConfigPath = launcherConfigPath;
        _http = http;
        _github = github;
    }

    public async Task<PatchStatus> CheckAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var game = ReadGameVersion(_launcherConfigPath);
        if (game is null)
        {
            problems.Add("ゲームのバージョンが分かりませんでした（XIVLauncher の設定にゲームのフォルダがありません）。");
        }

        string? dalamudVersion = null;
        try
        {
            dalamudVersion = await GetDalamudSupportedVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HubException ex)
        {
            problems.Add(ex.Message);
        }

        ReleaseInfo? act = null;
        try
        {
            act = await _github.GetLatestReleaseCachedAsync(ActPluginSource, cancellationToken).ConfigureAwait(false);
        }
        catch (HubException ex)
        {
            problems.Add($"FFXIV_ACT_Plugin: {ex.Message}");
        }

        return new PatchStatus(game, dalamudVersion, CompareDalamud(game, dalamudVersion),
            act?.TagName, act?.PublishedAt, CompareActPlugin(game, act?.PublishedAt), problems);
    }

    /// <summary>XIVLauncher の設定からゲームのフォルダを探し、ffxivgame.ver を読む。分からなければ null。</summary>
    public static string? ReadGameVersion(string launcherConfigPath)
    {
        try
        {
            var info = new FileInfo(launcherConfigPath);
            if (!info.Exists || info.Length > MaxLauncherConfigBytes)
            {
                return null;
            }
            using var document = JsonDocument.Parse(File.ReadAllText(launcherConfigPath));
            if (!document.RootElement.TryGetProperty("GamePath", out var gamePath) || gamePath.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            var versionFile = Path.Combine(gamePath.GetString()!, "game", GameVersionFile);
            if (!File.Exists(versionFile) || new FileInfo(versionFile).Length > 64)
            {
                return null;
            }
            var version = File.ReadAllText(versionFile).Trim();
            return ParseVersionDate(version) is null ? null : version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public async Task<string> GetDalamudSupportedVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(DalamudVersionInfoUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HubException($"Dalamud の対応状況を取得できませんでした（HTTP {(int)response.StatusCode}）。");
            }
            if (response.Content.Headers.ContentLength > MaxVersionInfoBytes)
            {
                throw new HubException("Dalamud の配布サーバーから想定外の応答がありました。");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("supportedGameVer", out var ver) && ver.ValueKind == JsonValueKind.String
                && ParseVersionDate(ver.GetString()) is not null)
            {
                return ver.GetString()!;
            }
            throw new HubException("Dalamud の配布サーバーから想定外の応答がありました。");
        }
        catch (HttpRequestException ex)
        {
            throw new HubException("Dalamud の配布サーバーに接続できませんでした。", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HubException("Dalamud の配布サーバーの応答がありませんでした。", ex);
        }
        catch (JsonException ex)
        {
            throw new HubException("Dalamud の配布サーバーから想定外の応答がありました。", ex);
        }
    }

    public static SupportState CompareDalamud(string? game, string? supported)
    {
        if (game is null || supported is null)
        {
            return SupportState.Unknown;
        }
        // バージョンは「年.月.日.番号.番号」の固定桁なので、文字列の大小がそのまま新旧になる
        var order = string.CompareOrdinal(game, supported);
        return order == 0 ? SupportState.Supported
            : order > 0 ? SupportState.NotYetSupported
            : SupportState.GameUpdateRequired;
    }

    public static SupportState CompareActPlugin(string? game, DateTimeOffset? published)
    {
        if (ParseVersionDate(game) is not { } built || published is null)
        {
            return SupportState.Unknown;
        }
        return DateOnly.FromDateTime(published.Value.UtcDateTime) >= built ? SupportState.Supported : SupportState.NotYetSupported;
    }

    /// <summary>画面に出す1行の説明。</summary>
    public static string Describe(PatchStatus status)
    {
        var parts = new List<string>
        {
            "ゲーム " + (status.GameVersion ?? "不明"),
            "Dalamud: " + status.Dalamud switch
            {
                SupportState.Supported => "対応済み",
                SupportState.NotYetSupported => "未対応（パッチ後の更新待ち。プラグインは読み込まれません）",
                SupportState.GameUpdateRequired => "ゲームの更新が必要です",
                _ => "確認できませんでした",
            },
        };
        var act = status.ActPluginVersion is null ? "FFXIV_ACT_Plugin" : $"FFXIV_ACT_Plugin {status.ActPluginVersion}";
        var published = status.ActPluginPublishedAt is { } at ? $"{at.ToLocalTime():M/d} 公開" : null;
        parts.Add(act + ": " + status.ActPlugin switch
        {
            SupportState.Supported => $"対応済み（{published}）",
            SupportState.NotYetSupported => $"未対応の可能性（パッチ後の更新がまだです。最新は {published}）",
            _ => "確認できませんでした",
        });
        return string.Join("　／　", parts);
    }

    /// <summary>注意が必要か（どちらかが今のゲームに対応していない）。</summary>
    public static bool NeedsAttention(PatchStatus status) =>
        status.Dalamud is SupportState.NotYetSupported or SupportState.GameUpdateRequired
        || status.ActPlugin is SupportState.NotYetSupported;

    /// <summary>「2026.10.06.0000.0000」の日付の部分。形式が違えば null。</summary>
    public static DateOnly? ParseVersionDate(string? version)
    {
        if (version is null || version.Length != "yyyy.MM.dd.0000.0000".Length)
        {
            return null;
        }
        return DateOnly.TryParseExact(version[..10], "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && version[10..].All(c => c == '.' || char.IsAsciiDigit(c))
            ? date
            : null;
    }
}
