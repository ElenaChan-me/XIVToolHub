using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using XIVToolHub.Core.GitHub;
using XIVToolHub.Core.Models;
using XIVToolHub.Core.Storage;

namespace XIVToolHub.Core.Prerequisites;

/// <summary>ツールの前提になるソフト（XIVToolHub の管理外で、Windows に入れるもの）。</summary>
public sealed record Prerequisite(string Id, string Name, string Description, bool NeedsAdmin);

/// <summary>カタログに書ける前提のソフトの一覧。</summary>
public static class PrerequisiteCatalog
{
    public const string VcRedistId = "vcredist-x64";
    public const string NetFrameworkId = "netfx48";
    public const string XivLauncherId = "xivlauncher";

    public static readonly IReadOnlyDictionary<string, Prerequisite> Known = new Dictionary<string, Prerequisite>
    {
        [VcRedistId] = new(VcRedistId, "Microsoft Visual C++ 再頒布可能パッケージ（x64）",
            "OverlayPlugin のオーバーレイの表示に必要です。", NeedsAdmin: true),
        [NetFrameworkId] = new(NetFrameworkId, ".NET Framework 4.8",
            "ACT の動作に必要です。Windows 10 の 2019 年以降の版と Windows 11 には最初から入っています。", NeedsAdmin: true),
        [XivLauncherId] = new(XivLauncherId, "XIVLauncher",
            "Dalamud プラグインの読み込みに必要です。インストール後、XIVLauncher で一度ゲームを起動してください。", NeedsAdmin: false),
    };
}

/// <summary>
/// ツールの前提になるソフトを調べ、無ければ公式の配布元から取得して入れる。
/// 入れるのは決まった配布元のものだけで、Microsoft の配布物は署名を確かめてから実行する。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PrerequisiteManager
{
    // Microsoft の公式の配布先（最新版に転送される）
    private const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    private const string NetFrameworkUrl = "https://go.microsoft.com/fwlink/?linkid=2088631";
    private const int NetFramework48Release = 528040;
    private static readonly GitHubSource XivLauncherSource = new()
    {
        Owner = "goatcorp",
        Repo = "FFXIVQuickLauncher",
        AssetPattern = "*Setup.exe",
    };

    // インストーラーが「成功」「再起動すれば完了」「もっと新しいものが入っている」を表す終了コード
    private static readonly HashSet<int> SuccessCodes = [0, 1638, 1641, 3010];
    private const int ErrorCancelled = 1223;
    private const long MaxInstallerBytes = 512L * 1024 * 1024;

    private readonly HubPaths _paths;
    private readonly HttpClient _http;
    private readonly GitHubReleaseClient _github;
    private readonly Func<string, bool> _probe;
    private readonly Func<string, string, CancellationToken, Task<int>> _run;
    private readonly Func<string, bool> _verifySignature;

    /// <param name="http">Microsoft の配布元から取得するクライアント。</param>
    /// <param name="probe">前提のソフトが入っているか。テストで差し替える。</param>
    /// <param name="run">インストーラーを実行して終了コードを返す。テストで差し替える。</param>
    /// <param name="verifySignature">ファイルの署名が正しいか。テストで差し替える。</param>
    public PrerequisiteManager(HubPaths paths, HttpClient http, GitHubReleaseClient github,
        Func<string, bool>? probe = null, Func<string, string, CancellationToken, Task<int>>? run = null, Func<string, bool>? verifySignature = null)
    {
        _paths = paths;
        _http = http;
        _github = github;
        _probe = probe ?? IsInstalledOnThisPc;
        _run = run ?? RunInstallerAsync;
        _verifySignature = verifySignature ?? HasValidSignature;
    }

    public bool IsInstalled(string id) => _probe(id);

    /// <summary>ツールの前提のうち、まだ入っていないもの。</summary>
    public IReadOnlyList<Prerequisite> Missing(ToolDefinition tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return [.. tool.Prerequisites.Where(PrerequisiteCatalog.Known.ContainsKey).Where(id => !_probe(id)).Select(id => PrerequisiteCatalog.Known[id])];
    }

    /// <summary>前提のソフトを取得して入れる。入ったことを確かめられなければ HubException。</summary>
    public async Task InstallAsync(Prerequisite prerequisite, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prerequisite);
        Directory.CreateDirectory(_paths.DownloadsDirectory);
        var installer = Path.Combine(_paths.DownloadsDirectory, $"{prerequisite.Id}-{Guid.NewGuid():N}.exe");
        try
        {
            string arguments;
            switch (prerequisite.Id)
            {
                case PrerequisiteCatalog.VcRedistId:
                    await DownloadAsync(VcRedistUrl, installer, progress, cancellationToken).ConfigureAwait(false);
                    arguments = "/install /passive /norestart";
                    break;
                case PrerequisiteCatalog.NetFrameworkId:
                    await DownloadAsync(NetFrameworkUrl, installer, progress, cancellationToken).ConfigureAwait(false);
                    arguments = "/passive /norestart";
                    break;
                case PrerequisiteCatalog.XivLauncherId:
                    var release = await _github.GetLatestReleaseAsync(XivLauncherSource, cancellationToken).ConfigureAwait(false);
                    var asset = GitHubReleaseClient.SelectAsset(release, XivLauncherSource.AssetPattern)
                        ?? throw new HubException("XIVLauncher のインストーラーが見つかりません。");
                    await _github.DownloadAssetAsync(XivLauncherSource, asset, installer, progress, cancellationToken).ConfigureAwait(false);
                    // XIVLauncher のインストーラーは自分の画面を出して進める
                    arguments = "";
                    break;
                default:
                    throw new HubException($"前提のソフト「{prerequisite.Id}」の入れ方が分かりません。");
            }

            if (prerequisite.Id != PrerequisiteCatalog.XivLauncherId && !_verifySignature(installer))
            {
                throw new HubException($"{prerequisite.Name} のインストーラーの署名を確かめられなかったため、実行しませんでした。");
            }

            int exitCode;
            try
            {
                exitCode = await _run(installer, arguments, cancellationToken).ConfigureAwait(false);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                throw new HubException($"{prerequisite.Name} のインストールがキャンセルされました。", ex);
            }
            if (!SuccessCodes.Contains(exitCode) && prerequisite.Id != PrerequisiteCatalog.XivLauncherId)
            {
                throw new HubException($"{prerequisite.Name} のインストールに失敗しました（終了コード {exitCode}）。");
            }
            if (!_probe(prerequisite.Id))
            {
                throw new HubException($"{prerequisite.Name} が入ったことを確かめられませんでした。インストーラーの画面を最後まで進めたか確認してください。");
            }
        }
        finally
        {
            try
            {
                File.Delete(installer);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 一時ファイルが残っても動作には影響しない
            }
        }
    }

    private async Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HubException($"ダウンロードできませんでした（HTTP {(int)response.StatusCode}）: {url}");
            }
            var total = response.Content.Headers.ContentLength;
            if (total > MaxInstallerBytes)
            {
                throw new HubException("インストーラーが想定より大きいため中断しました。");
            }
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = File.Create(destination);
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                written += read;
                if (written > MaxInstallerBytes)
                {
                    throw new HubException("インストーラーが想定より大きいため中断しました。");
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                if (total > 0)
                {
                    progress?.Report(Math.Min(1.0, (double)written / total.Value));
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new HubException($"インストーラーをダウンロードできませんでした。（{ex.Message}）", ex);
        }
    }

    /// <summary>この PC に入っているか。レジストリとファイルを見るだけで、何も変えない。</summary>
    public static bool IsInstalledOnThisPc(string id)
    {
        try
        {
            switch (id)
            {
                case PrerequisiteCatalog.VcRedistId:
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
                    return key?.GetValue("Installed") is int installed && installed == 1;
                }
                case PrerequisiteCatalog.NetFrameworkId:
                {
                    using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                    using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
                    return key?.GetValue("Release") is int release && release >= NetFramework48Release;
                }
                case PrerequisiteCatalog.XivLauncherId:
                {
                    // 今の XIVLauncher（Velopack）は current、以前の版（Squirrel）は app-* に入る
                    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "XIVLauncher");
                    if (File.Exists(Path.Combine(root, "current", "XIVLauncher.exe")))
                    {
                        return true;
                    }
                    if (Directory.Exists(root) && Directory.EnumerateDirectories(root, "app-*").Any(d => File.Exists(Path.Combine(d, "XIVLauncher.exe"))))
                    {
                        return true;
                    }
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\XIVLauncher");
                    return key is not null;
                }
                default:
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // 調べられないときは入っているとみなし、ツールのインストールを止めない
            return true;
        }
    }

    private static async Task<int> RunInstallerAsync(string path, string arguments, CancellationToken cancellationToken)
    {
        // UseShellExecute にすると、インストーラーが管理者の権限を求めたときに確認の画面が出る
        using var process = Process.Start(new ProcessStartInfo(path, arguments) { UseShellExecute = true })
            ?? throw new HubException("インストーラーを起動できませんでした。");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    /// <summary>Windows に署名を確かめてもらう（改ざんや配布元の取り違えを防ぐ）。</summary>
    public static bool HasValidSignature(string path) => WinTrust.Verify(path);

    private static class WinTrust
    {
        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        private const uint UiNone = 2;
        private const uint RevokeNone = 0;
        private const uint ChoiceFile = 1;
        private const uint StateIgnore = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileInfo
        {
            public uint CbStruct;
            public string FilePath;
            public IntPtr File;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TrustData
        {
            public uint CbStruct;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr File;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProvFlags;
            public uint UiContext;
            public IntPtr SignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref TrustData data);

        public static bool Verify(string path)
        {
            var file = new FileInfo { CbStruct = (uint)Marshal.SizeOf<FileInfo>(), FilePath = path };
            var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
            try
            {
                Marshal.StructureToPtr(file, filePtr, false);
                var data = new TrustData
                {
                    CbStruct = (uint)Marshal.SizeOf<TrustData>(),
                    UiChoice = UiNone,
                    RevocationChecks = RevokeNone,
                    UnionChoice = ChoiceFile,
                    File = filePtr,
                    StateAction = StateIgnore,
                };
                return WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, ref data) == 0;
            }
            finally
            {
                Marshal.DestroyStructure<FileInfo>(filePtr);
                Marshal.FreeHGlobal(filePtr);
            }
        }
    }
}
