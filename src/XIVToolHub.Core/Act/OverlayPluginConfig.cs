using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using XIVToolHub.Core.Install;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Act;

/// <summary>
/// OverlayPlugin の設定（ポータブル版 ACT の Config\RainbowMage.OverlayPlugin.config.json）を整える。
/// Triggevent Plus は OverlayPlugin の WebSocket サーバーにつなぐので、ACT を起動したら自動でサーバーが始まるようにしておく。
/// 利用者が変えたポートなどはそのまま残し、足りない項目だけを補う。
/// </summary>
public static class OverlayPluginConfig
{
    public const string LocalAddress = "127.0.0.1";
    private const long MaxConfigBytes = 16L * 1024 * 1024;

    public static string PathFor(string actDirectory) =>
        Path.Combine(actDirectory, ActConfigEditor.ConfigFolderName, OverlayWebSocketSettings.ConfigFileName);

    /// <summary>
    /// WebSocket サーバーを自動で開始する設定にする。変更したら true。
    /// ACT（OverlayPlugin）は終了時に設定を書き戻すので、ACT が起動していないときに呼ぶこと。
    /// </summary>
    public static bool EnableWebSocket(string actDirectory, TimeProvider clock)
    {
        var path = PathFor(actDirectory);
        JsonObject root;
        var exists = File.Exists(path);
        if (exists)
        {
            if (new FileInfo(path).Length > MaxConfigBytes)
            {
                throw new HubException("OverlayPlugin の設定ファイルが大きすぎます。");
            }
            try
            {
                root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                    ?? throw new HubException("OverlayPlugin の設定ファイルの形式が想定と異なります。");
            }
            catch (JsonException ex)
            {
                throw new HubException("OverlayPlugin の設定ファイルを読み込めません。ファイルが壊れている可能性があります。", ex);
            }
        }
        else
        {
            // まだ ACT を起動していない場合。OverlayPlugin は足りない項目を既定値で補って読み込む
            root = [];
        }

        var changed = false;
        if (root["WSServerRunning"]?.GetValueKind() != JsonValueKind.True)
        {
            root["WSServerRunning"] = true;
            changed = true;
        }
        if (root["WSServerIP"] is not JsonValue ip || ip.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(ip.GetValue<string>()))
        {
            // 外から接続されないよう、この PC だけで待ち受ける
            root["WSServerIP"] = LocalAddress;
            changed = true;
        }
        if (root["WSServerPort"]?.GetValueKind() != JsonValueKind.Number)
        {
            root["WSServerPort"] = OverlayWebSocketSettings.DefaultPort;
            changed = true;
        }
        if (root["WSServerSSL"]?.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
        {
            root["WSServerSSL"] = false;
            changed = true;
        }
        if (!changed)
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (exists)
        {
            var stamp = clock.GetLocalNow().ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            File.Copy(path, $"{path}.xivtoolhub-{stamp}.bak", overwrite: true);
        }
        AtomicFile.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }
}
