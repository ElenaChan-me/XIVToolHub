using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Install;

/// <param name="Fraction">このツールの処理全体のうち、終わった割合（0〜1）。</param>
public sealed record InstallProgress(string ToolId, string Stage, double? Fraction);

public sealed record InstallResult(string ToolId, string Version, bool AlreadyLatest, bool DevModeEnabled);

/// <summary>
/// ツールのインストール・更新・アンインストールを行う。
/// 新しいファイルを別の場所で用意してから入れ替えるので、途中で失敗しても元のバージョンは残る。
/// </summary>
public sealed class ToolInstaller
{
    // 1つのツールの処理のうち、各段階が始まる時点の割合
    private const double ProgressChecking = 0.02;
    private const double ProgressDownloadStart = 0.05;
    private const double ProgressDownloadEnd = 0.85;
    private const double ProgressInstalling = 0.95;

    private readonly HubPaths _paths;
    private readonly GitHubReleaseClient _github;
    private readonly InstalledStore _installed;
    private readonly DalamudConfigEditor _dalamud;
    private readonly ActConfigEditor _act;
    private readonly IProcessDetector _gameDetector;
    private readonly TimeProvider _clock;
    private readonly Func<string?, string?> _resolveFontPath;
    private readonly Func<string> _currentTheme;
    private readonly Func<IReadOnlyList<string>> _voicePacks;

    /// <param name="resolveFontPath">フォント ID から取得済みのフォントファイルを探す。未取得なら null を返す。</param>
    /// <param name="currentTheme">ツールに伝える現在のテーマ。</param>
    /// <param name="voicePacks">インストール済みのボイスパックのフォルダ。</param>
    public ToolInstaller(HubPaths paths, GitHubReleaseClient github, InstalledStore installed, DalamudConfigEditor dalamud, ActConfigEditor act, IProcessDetector gameDetector, TimeProvider clock,
        Func<string?, string?>? resolveFontPath = null, Func<string>? currentTheme = null, Func<IReadOnlyList<string>>? voicePacks = null)
    {
        _voicePacks = voicePacks ?? (() => []);
        _resolveFontPath = resolveFontPath ?? (_ => null);
        _currentTheme = currentTheme ?? (() => HubThemes.Default);
        _paths = paths;
        _github = github;
        _installed = installed;
        _dalamud = dalamud;
        _act = act;
        _gameDetector = gameDetector;
        _clock = clock;
    }

    public Task<InstallResult> InstallAsync(ToolDefinition tool, bool force, IProgress<InstallProgress>? progress, CancellationToken cancellationToken) =>
        InstallAsync(tool, force, null, progress, cancellationToken);

    /// <param name="preferences">表示言語とフォント。null なら前回の設定を引き継ぎ、初回は既定値にする。</param>
    public async Task<InstallResult> InstallAsync(ToolDefinition tool, bool force, ToolPreferences? preferences, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (ToolCatalog.Validate(tool) is { Count: > 0 } errors)
        {
            throw new HubException(string.Join(Environment.NewLine, errors));
        }
        void Report(string stage, double? fraction = null) => progress?.Report(new InstallProgress(tool.Id, stage, fraction));

        // ゲーム起動中は DLL が使用中で入れ替えられず、Dalamud の設定も上書きされるため先に止める
        if (tool.Kind == ToolKind.DalamudPlugin)
        {
            EnsurePluginEditable();
        }
        else if (tool.Kind == ToolKind.ActPlugin)
        {
            // ACT 起動中は DLL が使用中で入れ替えられず、設定も終了時に上書きされる
            _act.EnsureEditable();
        }

        Report("リリース情報を確認しています", ProgressChecking);
        var release = await _github.GetLatestReleaseAsync(tool.Source, cancellationToken).ConfigureAwait(false);
        var asset = GitHubReleaseClient.SelectAsset(release, tool.Source.AssetPattern)
            ?? throw new HubException($"リリース {release.TagName} に「{tool.Source.AssetPattern}」に一致するファイルがありません。");

        var current = _installed.Find(tool.Id);
        var effective = ResolvePreferences(tool, current, preferences);
        if (!force && current is not null && current.Version == release.TagName && File.Exists(Path.Combine(current.InstallDirectory, tool.EntryFile)))
        {
            if (preferences is not null && effective is not null)
            {
                ApplySettings(tool, effective);
            }
            Report("最新版がインストールされています", 1);
            return new InstallResult(tool.Id, release.TagName, AlreadyLatest: true, DevModeEnabled: false);
        }

        Directory.CreateDirectory(_paths.DownloadsDirectory);
        var downloadPath = Path.Combine(_paths.DownloadsDirectory, $"{tool.Id}-{Guid.NewGuid():N}.zip");
        // JRE を同梱したツールは階層が深いので、作業用のフォルダ名は短くする
        var stagingPath = Path.Combine(_paths.StagingDirectory, Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Report($"{asset.Name} をダウンロードしています", ProgressDownloadStart);
            var downloadProgress = new Progress<double>(f =>
                Report($"{asset.Name} をダウンロードしています（{f:P0}）", ProgressDownloadStart + (ProgressDownloadEnd - ProgressDownloadStart) * f));
            await _github.DownloadAssetAsync(tool.Source, asset, downloadPath, downloadProgress, cancellationToken).ConfigureAwait(false);

            Report("展開しています", ProgressDownloadEnd);
            cancellationToken.ThrowIfCancellationRequested();
            SafeZipExtractor.Extract(downloadPath, stagingPath);
            VerifyContents(tool, stagingPath);

            Report("インストールしています", ProgressInstalling);
            var installDirectory = _paths.ToolDirectory(tool.Id);
            var entryPath = Path.Combine(installDirectory, tool.EntryFile);
            var devModeEnabled = false;

            ReplaceDirectory(stagingPath, installDirectory, () =>
            {
                // 入れ替え後のフォルダには設定ファイルが無いので、更新のたびに書き直す
                if (effective is not null)
                {
                    ToolSettingsFile.Write(installDirectory, tool, effective, _resolveFontPath(effective.FontId), _currentTheme(), _voicePacks());
                }
                switch (tool.Kind)
                {
                    case ToolKind.DalamudPlugin:
                        devModeEnabled = _dalamud.Register(entryPath).DevModeEnabled;
                        break;
                    case ToolKind.ActPlugin:
                        _act.Register(entryPath);
                        // Triggevent Plus などがすぐ使えるよう、WebSocket サーバーを自動で始める設定にしておく
                        if (tool.Id == ToolCatalog.OverlayPluginToolId)
                        {
                            OverlayPluginConfig.EnableWebSocket(_act.ActDirectory, _clock);
                        }
                        break;
                }
            });

            _installed.Set(new InstalledTool
            {
                ToolId = tool.Id,
                Version = release.TagName,
                AssetName = asset.Name,
                InstallDirectory = installDirectory,
                RegisteredPluginPath = tool.Kind is ToolKind.DalamudPlugin or ToolKind.ActPlugin ? entryPath : null,
                InstalledAt = _clock.GetLocalNow(),
                Language = effective?.Language,
                FontId = effective?.FontId,
            });

            Report("完了しました", 1);
            return new InstallResult(tool.Id, release.TagName, AlreadyLatest: false, devModeEnabled);
        }
        finally
        {
            TryDeleteFile(downloadPath);
            TryDeleteDirectory(stagingPath);
        }
    }

    /// <summary>
    /// インストール済みツールの表示言語とフォントを変更する。テーマも現在のものを書き直す。ツールは次回の起動時に読み込む。
    /// </summary>
    public void ApplySettings(ToolDefinition tool, ToolPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(preferences);
        if (!tool.SupportsLanguage)
        {
            throw new HubException($"{tool.Name} は表示言語の変更に対応していません。");
        }
        var current = _installed.Find(tool.Id) ?? throw new HubException($"{tool.Name} はインストールされていません。");
        if (!File.Exists(Path.Combine(current.InstallDirectory, tool.EntryFile)))
        {
            throw new HubException($"{tool.Name} のファイルが見つかりません。もう一度インストールしてください。");
        }
        var fontId = NormalizeFont(tool, preferences);
        var normalized = preferences with { FontId = fontId };
        ToolSettingsFile.Write(current.InstallDirectory, tool, normalized, _resolveFontPath(fontId), _currentTheme(), _voicePacks());
        _installed.Set(current with { Language = normalized.Language, FontId = fontId });
    }

    /// <summary>
    /// テーマを変えたときなどに、インストール済みのツールの設定ファイルを今の設定で書き直す。
    /// </summary>
    public void RefreshSettings(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!tool.SupportsLanguage || _installed.Find(tool.Id) is not { } current)
        {
            return;
        }
        ApplySettings(tool, ResolvePreferences(tool, current, null)!);
    }

    public void Uninstall(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        var current = _installed.Find(tool.Id);
        var installDirectory = _paths.ToolDirectory(tool.Id);

        if (current?.RegisteredPluginPath is { } pluginPath)
        {
            if (tool.Kind == ToolKind.ActPlugin)
            {
                // ACT ごと削除済みなら設定ファイルも残っていないので、登録の解除は不要
                if (_act.ActInstalled)
                {
                    _act.Unregister(pluginPath);
                }
            }
            else
            {
                EnsurePluginEditable();
                _dalamud.Unregister(pluginPath);
            }
        }

        if (Directory.Exists(installDirectory))
        {
            try
            {
                Directory.Delete(installDirectory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new HubException("ファイルを削除できませんでした。ツールやゲームを終了してからお試しください。", ex);
            }
        }
        _installed.Remove(tool.Id);
    }

    public async Task<ReleaseInfo> GetLatestAsync(ToolDefinition tool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return await _github.GetLatestReleaseCachedAsync(tool.Source, cancellationToken).ConfigureAwait(false);
    }

    private static ToolPreferences? ResolvePreferences(ToolDefinition tool, InstalledTool? current, ToolPreferences? requested)
    {
        if (!tool.SupportsLanguage)
        {
            return null;
        }
        var candidate = requested
            ?? (current?.Language is { } language ? new ToolPreferences(language, current.FontId) : null);
        if (candidate is null || !tool.Languages.Contains(candidate.Language))
        {
            // カタログ側で言語が減った場合なども既定値に戻す
            candidate = ToolPreferences.DefaultFor(tool);
        }
        return candidate with { FontId = NormalizeFont(tool, candidate) };
    }

    /// <summary>フォントは日本語表示かつ対応ツールのときだけ、カタログにある ID を残す。</summary>
    private static string? NormalizeFont(ToolDefinition tool, ToolPreferences preferences)
    {
        if (!tool.SupportsFont || preferences.Language != ToolLanguages.Japanese || preferences.FontId is null)
        {
            return null;
        }
        return Fonts.FontCatalog.Find(preferences.FontId) is null ? null : preferences.FontId;
    }

    private void EnsurePluginEditable()
    {
        if (!_dalamud.ConfigExists)
        {
            throw new HubException(
                $"Dalamud の設定ファイルが見つかりません。XIVLauncher で一度ゲームを起動してから、もう一度お試しください。\n{_dalamud.ConfigPath}");
        }
        if (_gameDetector.IsRunning())
        {
            throw new HubException("ゲームの起動中は Dalamud プラグインを変更できません。ゲームを終了してからお試しください。");
        }
    }

    private static void VerifyContents(ToolDefinition tool, string stagingPath)
    {
        var entry = Path.Combine(stagingPath, tool.EntryFile);
        if (!File.Exists(entry))
        {
            throw new HubException($"ダウンロードしたファイルに {tool.EntryFile} が含まれていません。カタログの設定を確認してください。");
        }
        if (tool.Kind == ToolKind.DalamudPlugin && !File.Exists(Path.ChangeExtension(entry, ".json")))
        {
            // Dalamud はマニフェストのない開発用プラグインを読み込まない
            throw new HubException($"{Path.ChangeExtension(tool.EntryFile, ".json")}（プラグインのマニフェスト）が含まれていません。");
        }
    }

    /// <summary>
    /// 用意したフォルダを本来の場所へ入れ替える。afterSwap が失敗した場合は元のフォルダに戻す。
    /// </summary>
    private static void ReplaceDirectory(string source, string destination, Action afterSwap)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var backup = destination + $".old-{Guid.NewGuid():N}";
        var hadPrevious = Directory.Exists(destination);

        try
        {
            if (hadPrevious)
            {
                Directory.Move(destination, backup);
            }
            Directory.Move(source, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (hadPrevious && !Directory.Exists(destination) && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }
            throw new HubException("ファイルを入れ替えられませんでした。ツールやゲームを終了してからお試しください。", ex);
        }

        try
        {
            afterSwap();
        }
        catch
        {
            TryDeleteDirectory(destination);
            if (hadPrevious && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }
            throw;
        }

        if (hadPrevious)
        {
            TryDeleteDirectory(backup);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 一時ファイルが残っても次回の処理には影響しない
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 古いフォルダが残っても動作には影響しない
        }
    }
}
