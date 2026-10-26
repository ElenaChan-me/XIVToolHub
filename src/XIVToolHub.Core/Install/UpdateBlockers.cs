using XIVToolHub.Core.Catalog;
using XIVToolHub.Core.Dalamud;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Core.Install;

/// <summary>
/// 起動中で、ツールの更新の妨げになっているアプリを調べる。
/// ACT の起動中は ACT とそのプラグインの DLL が、Triggevent Plus の起動中は本体とボイスパックのファイルが使用中で入れ替えられず、
/// ゲームの起動中は Dalamud の設定が上書きされる。
/// </summary>
public sealed class UpdateBlockers
{
    public const string ActName = "ACT";
    public const string TriggeventName = "Triggevent Plus";
    public const string GameName = "FINAL FANTASY XIV";

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly IProcessDetector _act;
    private readonly IProcessDetector _triggevent;
    private readonly IProcessDetector _game;

    public UpdateBlockers(IProcessDetector act, IProcessDetector triggevent, IProcessDetector game)
    {
        _act = act;
        _triggevent = triggevent;
        _game = game;
    }

    /// <summary>今、更新の妨げになっているアプリの名前。</summary>
    public IReadOnlyList<string> Blocking(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var list = tools.ToList();
        var result = new List<string>();
        if (list.Any(t => t.Id == ToolCatalog.ActToolId || t.Kind == ToolKind.ActPlugin) && _act.IsRunning())
        {
            result.Add(ActName);
        }
        if (list.Any(t => t.Id == ToolCatalog.TriggeventToolId || t.Kind == ToolKind.VoicePack) && _triggevent.IsRunning())
        {
            result.Add(TriggeventName);
        }
        if (list.Any(t => t.Kind == ToolKind.DalamudPlugin) && _game.IsRunning())
        {
            result.Add(GameName);
        }
        return result;
    }

    /// <summary>
    /// 妨げになっているアプリが終わるまで待つ。終わったら true、時間切れなら false。
    /// </summary>
    /// <param name="waiting">待ち始めたときと、待つ相手が変わったときに、待っているアプリの名前を知らせる。</param>
    public async Task<bool> WaitAsync(IReadOnlyList<ToolDefinition> tools, Action<IReadOnlyList<string>>? waiting, TimeSpan pollInterval, TimeSpan timeout,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var deadline = clock.GetUtcNow() + timeout;
        string? last = null;
        while (true)
        {
            var blocking = Blocking(tools);
            if (blocking.Count == 0)
            {
                return true;
            }
            var key = string.Join(",", blocking);
            if (key != last)
            {
                last = key;
                waiting?.Invoke(blocking);
            }
            if (clock.GetUtcNow() >= deadline)
            {
                return false;
            }
            await Task.Delay(pollInterval, clock, cancellationToken).ConfigureAwait(false);
        }
    }
}
