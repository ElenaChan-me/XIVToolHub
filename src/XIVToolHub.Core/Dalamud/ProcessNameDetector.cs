using System.Diagnostics;

namespace XIVToolHub.Core.Dalamud;

public interface IProcessDetector
{
    bool IsRunning();
}

/// <summary>
/// プロセス名だけで起動中かを判定する。対象のプロセスには一切アクセスしない。
/// </summary>
public sealed class ProcessNameDetector(params string[] processNames) : IProcessDetector
{
    public static ProcessNameDetector Game { get; } = new("ffxiv_dx11", "ffxiv");

    public static ProcessNameDetector Act { get; } = new("Advanced Combat Tracker", "ACTx86");

    public bool IsRunning()
    {
        foreach (var name in processNames)
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
