using XIVToolHub.Core.Act;
using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Install;

public sealed record InstallProgress(string ToolId, string Stage, double? Fraction);

public sealed record InstallResult(string ToolId, string Version, bool AlreadyLatest, bool DevModeEnabled);

/// <summary>
/// ツールのインストール・更新・アンインストールを行う。
/// 新しいファイルを別の場所で用意してから入れ替えるので、途中で失敗しても元のバージョンは残る。
/// </summary>
public sealed class ToolInstaller
{
    private readonly HubPaths _paths;
    private readonly GitHubReleaseClient _github;
    private readonly InstalledStore _installed;
    private readonly DalamudConfigEditor _dalamud;
    private readonly ActConfigEditor _act;
    private readonly IProcessDetector _gameDetector;
    private readonly TimeProvider _clock;

    public ToolInstaller(HubPaths paths, GitHubReleaseClient github, InstalledStore installed, DalamudConfigEditor dalamud, ActConfigEditor act, IProcessDetector gameDetector, TimeProvider clock)
    {
        _paths = paths;
        _github = github;
        _installed = installed;
        _dalamud = dalamud;
        _act = act;
        _gameDetector = gameDetector;
        _clock = clock;
    }

    public async Task<InstallResult> InstallAsync(ToolDefinition tool, bool force, IProgress<InstallProgress>? progress, CancellationToken cancellationToken)
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

        Report("リリース情報を確認しています");
        var release = await _github.GetLatestReleaseAsync(tool.Source, cancellationToken).ConfigureAwait(false);
        var asset = GitHubReleaseClient.SelectAsset(release, tool.Source.AssetPattern)
            ?? throw new HubException($"リリース {release.TagName} に「{tool.Source.AssetPattern}」に一致するファイルがありません。");

        var current = _installed.Find(tool.Id);
        if (!force && current is not null && current.Version == release.TagName && File.Exists(Path.Combine(current.InstallDirectory, tool.EntryFile)))
        {
            Report("最新版がインストールされています", 1);
            return new InstallResult(tool.Id, release.TagName, AlreadyLatest: true, DevModeEnabled: false);
        }

        Directory.CreateDirectory(_paths.DownloadsDirectory);
        var downloadPath = Path.Combine(_paths.DownloadsDirectory, $"{tool.Id}-{Guid.NewGuid():N}.zip");
        var stagingPath = Path.Combine(_paths.StagingDirectory, $"{tool.Id}-{Guid.NewGuid():N}");
        try
        {
            Report($"{asset.Name} をダウンロードしています", 0);
            var downloadProgress = new Progress<double>(f => Report($"{asset.Name} をダウンロードしています", f));
            await _github.DownloadAssetAsync(tool.Source, asset, downloadPath, downloadProgress, cancellationToken).ConfigureAwait(false);

            Report("展開しています");
            cancellationToken.ThrowIfCancellationRequested();
            SafeZipExtractor.Extract(downloadPath, stagingPath);
            VerifyContents(tool, stagingPath);

            Report("インストールしています");
            var installDirectory = _paths.ToolDirectory(tool.Id);
            var entryPath = Path.Combine(installDirectory, tool.EntryFile);
            var devModeEnabled = false;

            ReplaceDirectory(stagingPath, installDirectory, () =>
            {
                switch (tool.Kind)
                {
                    case ToolKind.DalamudPlugin:
                        devModeEnabled = _dalamud.Register(entryPath).DevModeEnabled;
                        break;
                    case ToolKind.ActPlugin:
                        _act.Register(entryPath);
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
        return await _github.GetLatestReleaseAsync(tool.Source, cancellationToken).ConfigureAwait(false);
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
