using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using XIVToolHub.Core.Act;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Core.Install;

/// <summary>OverlayPlugin の WebSocket サーバーの設定。</summary>
public sealed record OverlayWebSocketSettings(int Port, bool Running)
{
    public const string ConfigFileName = "RainbowMage.OverlayPlugin.config.json";
    public const int DefaultPort = 10501;
    private const long MaxConfigBytes = 16L * 1024 * 1024;

    /// <summary>ポータブル版 ACT の設定から読む。設定が無いときは OverlayPlugin の既定値。</summary>
    public static OverlayWebSocketSettings Read(string actDirectory)
    {
        var path = Path.Combine(actDirectory, ActConfigEditor.ConfigFolderName, ConfigFileName);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxConfigBytes)
            {
                return new OverlayWebSocketSettings(DefaultPort, Running: false);
            }
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            var port = root.TryGetProperty("WSServerPort", out var p) && p.TryGetInt32(out var value) && value is > 0 and <= IPEndPoint.MaxPort
                ? value
                : DefaultPort;
            var running = root.TryGetProperty("WSServerRunning", out var r) && r.ValueKind == JsonValueKind.True;
            return new OverlayWebSocketSettings(port, running);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new OverlayWebSocketSettings(DefaultPort, Running: false);
        }
    }
}

/// <summary>
/// ACT と Triggevent Plus を正しい順番で起動する。
/// Triggevent Plus は OverlayPlugin の WebSocket に接続するので、ACT を起動してサーバーが待ち受けを始めてから起動する。
/// すでに起動しているものは起動し直さない。
/// </summary>
public sealed class LaunchSequence
{
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(1);

    private readonly Action<ToolDefinition> _launch;
    private readonly IProcessDetector _act;
    private readonly IProcessDetector _triggevent;
    private readonly Func<int, CancellationToken, Task<bool>> _probe;
    private readonly TimeProvider _clock;

    /// <param name="launch">ツールを起動する処理（ToolLauncher.Launch）。</param>
    /// <param name="probe">この PC のポートが待ち受けているか調べる。テストで差し替える。</param>
    public LaunchSequence(Action<ToolDefinition> launch, IProcessDetector act, IProcessDetector triggevent, TimeProvider clock,
        Func<int, CancellationToken, Task<bool>>? probe = null)
    {
        _launch = launch;
        _act = act;
        _triggevent = triggevent;
        _clock = clock;
        _probe = probe ?? IsListeningAsync;
    }

    /// <returns>利用者に伝えること（起動したもの・注意）。</returns>
    public async Task<IReadOnlyList<string>> RunAsync(ToolDefinition act, string actDirectory, ToolDefinition triggevent, TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var messages = new List<string>();
        if (_act.IsRunning())
        {
            messages.Add("ACT はすでに起動しています。");
        }
        else
        {
            _launch(act);
            messages.Add("ACT を起動しました。");
        }

        var ws = OverlayWebSocketSettings.Read(actDirectory);
        if (!ws.Running)
        {
            // 待っても開始されないので、待たずに知らせる
            messages.Add($"OverlayPlugin の WebSocket サーバーが開始されていません。ACT の「Plugins」→「OverlayPlugin WSServer」で「開始」を押してください（ポート {ws.Port}）。");
        }
        else if (!await WaitForPortAsync(ws.Port, waitTimeout, cancellationToken).ConfigureAwait(false))
        {
            messages.Add($"OverlayPlugin の WebSocket（ポート {ws.Port}）に {waitTimeout.TotalSeconds:0} 秒待ってもつながりませんでした。ACT の起動が終わってから Triggevent Plus の接続を確認してください。");
        }

        if (_triggevent.IsRunning())
        {
            messages.Add("Triggevent Plus はすでに起動しています。");
        }
        else
        {
            _launch(triggevent);
            messages.Add("Triggevent Plus を起動しました。");
        }
        return messages;
    }

    private async Task<bool> WaitForPortAsync(int port, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            if (await _probe(port, cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
            if (_clock.GetUtcNow() >= deadline)
            {
                return false;
            }
            await Task.Delay(PollInterval, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>この PC（127.0.0.1）のポートに接続できるか。</summary>
    public static async Task<bool> IsListeningAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>
/// 指定したフォルダの中の実行ファイルから起動したプロセスがあるか。
/// Triggevent Plus は同梱の Java（javaw.exe）で動くので、プロセス名ではなく場所で見分ける。
/// </summary>
public sealed class ProcessInFolderDetector(string processName, string folder) : IProcessDetector
{
    public bool IsRunning()
    {
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        var processes = Process.GetProcessesByName(processName);
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.MainModule?.FileName is { } path && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    // 権限の違うプロセスや終了したプロセスは対象外
                }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
