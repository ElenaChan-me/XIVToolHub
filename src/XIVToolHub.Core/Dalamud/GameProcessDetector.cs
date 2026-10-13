using System.Diagnostics;

namespace XIVToolHub.Core.Dalamud;

public interface IGameProcessDetector
{
    bool IsGameRunning();
}

/// <summary>
/// プロセス名だけでゲームの起動を判定する。ゲームのプロセスには一切アクセスしない。
/// </summary>
public sealed class GameProcessDetector : IGameProcessDetector
{
    private static readonly string[] GameProcessNames = ["ffxiv_dx11", "ffxiv"];

    public bool IsGameRunning()
    {
        foreach (var name in GameProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        return false;
    }
}
