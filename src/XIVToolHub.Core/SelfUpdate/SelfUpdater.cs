using System.Runtime.Versioning;
using Microsoft.Win32;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.SelfUpdate;

/// <summary>
/// XIVToolHub 自身を最新のリリースに入れ替える。
/// <para>
/// Windows では起動中の exe を上書きできないが、名前は変えられる。そこで今の exe を .old に退避してから新しい exe を置き、
/// 再起動したあとで .old を消す。配布物は exe 1つだけ（単一ファイル）なので、入れ替えるのもこのファイルだけでよい。
/// </para>
/// </summary>
public sealed class SelfUpdater
{
    public const string ExeName = "XIVToolHub.exe";
    public const string OldSuffix = ".old";

    /// <summary>インストーラー（installer/XIVToolHub.iss）の AppId。「アプリと機能」に出る版を、入れ替えた版に合わせるのに使う。</summary>
    public const string InstallerAppId = "{316C7E01-90F4-4529-AB38-66106DD0AC6B}_is1";

    public static readonly GitHubSource Source = new()
    {
        Owner = "ElenaChan-me",
        Repo = "XIVToolHub",
        AssetPattern = "XIVToolHub-win-x64.zip",
    };

    private readonly GitHubReleaseClient _github;
    private readonly HubPaths _paths;
    private readonly string _exePath;
    private readonly Version _current;

    public SelfUpdater(GitHubReleaseClient github, HubPaths paths, string exePath, Version current)
    {
        _github = github;
        _paths = paths;
        _exePath = Path.GetFullPath(exePath);
        _current = current;
    }

    public Version CurrentVersion => _current;

    /// <summary>
    /// 配布した単一ファイルの exe から起動しているか。開発中のビルド（dll が並ぶフォルダ）を exe 1つで上書きしないようにする。
    /// </summary>
    public static bool CanUpdate(string? exePath) =>
        exePath is not null
        && Path.GetFileName(exePath).StartsWith(Path.GetFileNameWithoutExtension(ExeName), StringComparison.OrdinalIgnoreCase)
        && exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !File.Exists(Path.Combine(Path.GetDirectoryName(exePath)!, "XIVToolHub.dll"));

    /// <summary>前回の更新で退避した古い exe を消す。起動直後に呼ぶ。</summary>
    public static void CleanupOld(string exePath)
    {
        try
        {
            var old = exePath + OldSuffix;
            if (File.Exists(old))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 古いプロセスがまだ終わっていなければ次回消す
        }
    }

    /// <summary>新しいリリースがあれば返す。無ければ null。</summary>
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        var release = await _github.GetLatestReleaseAsync(Source, cancellationToken).ConfigureAwait(false);
        return ParseVersion(release.TagName) is { } latest && latest > _current ? release : null;
    }

    /// <summary>「v0.4.1」のようなタグをバージョンにする。読めなければ null。</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }
        var text = tag.Trim().TrimStart('v', 'V');
        var end = text.IndexOfAny(['-', '+']);
        if (end >= 0)
        {
            text = text[..end];
        }
        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>0.4.1 と 0.4.1.0 を同じとみなすため、省略された桁を 0 にそろえる。</summary>
    public static Version Normalize(Version v) =>
        new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>
    /// 新しい exe をダウンロードして今の exe と入れ替える。戻り値は起動し直す exe のパス。
    /// 失敗した場合は今の exe をそのまま残す。
    /// </summary>
    public async Task<string> InstallAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        var asset = GitHubReleaseClient.SelectAsset(release, Source.AssetPattern)
            ?? throw new HubException($"リリース {release.TagName} に {Source.AssetPattern} がありません。");
        Directory.CreateDirectory(_paths.DownloadsDirectory);
        var zip = Path.Combine(_paths.DownloadsDirectory, $"xivtoolhub-{Guid.NewGuid():N}.zip");
        var staging = Path.Combine(_paths.StagingDirectory, $"xivtoolhub-{Guid.NewGuid():N}");
        try
        {
            await _github.DownloadAssetAsync(Source, asset, zip, progress, cancellationToken).ConfigureAwait(false);
            SafeZipExtractor.Extract(zip, staging);
            var downloaded = Path.Combine(staging, ExeName);
            if (!File.Exists(downloaded))
            {
                throw new HubException($"ダウンロードしたファイルに {ExeName} が含まれていません。");
            }
            Swap(downloaded, _exePath);
            if (OperatingSystem.IsWindows() && ParseVersion(release.TagName) is { } version)
            {
                UpdateInstalledVersion(version);
            }
            return _exePath;
        }
        finally
        {
            TryDelete(() => File.Delete(zip));
            TryDelete(() => Directory.Delete(staging, recursive: true));
        }
    }

    /// <summary>
    /// インストーラーで入れた場合、「アプリと機能」に出る版を新しい版に書き換える（XIVToolHub 自身の更新ではインストーラーを通らないため）。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void UpdateInstalledVersion(Version version)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}", writable: true);
            if (key is null)
            {
                return;
            }
            var text = version.ToString(3);
            key.SetValue("DisplayVersion", text);
            key.SetValue("DisplayName", "XIVToolHub");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            // 表示の版が古いままでも動作には影響しない
        }
    }

    /// <summary>今の exe を .old に退避して新しい exe を置く。置けなかったら元に戻す。</summary>
    public static void Swap(string newExe, string exePath)
    {
        var old = exePath + OldSuffix;
        try
        {
            if (File.Exists(old))
            {
                File.Delete(old);
            }
            File.Move(exePath, old);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new HubException("XIVToolHub を入れ替えられませんでした。書き込みできるフォルダに置いているか確認してください。", ex);
        }
        try
        {
            File.Move(newExe, exePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            File.Move(old, exePath);
            throw new HubException("XIVToolHub を入れ替えられませんでした。今のバージョンのまま使えます。", ex);
        }
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            // 一時ファイルが残っても動作には影響しない
        }
    }
}
