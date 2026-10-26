using XIVToolHub.Core.Install;
using XIVToolHub.Core.Models;

namespace XIVToolHub.Tests;

public class UpdateBlockersTests
{
    private static readonly ToolDefinition ActPlugin = TestData.Plugin("ffxiv-act-plugin") with { Kind = ToolKind.ActPlugin, DependsOn = ["act"] };
    private static readonly ToolDefinition Dalamud = TestData.Plugin();

    [Fact]
    public void ReportsOnlyAppsThatBlockTheSelectedTools()
    {
        var act = new FakeGameDetector { Running = true };
        var triggevent = new FakeGameDetector { Running = true };
        var game = new FakeGameDetector { Running = true };
        var blockers = new UpdateBlockers(act, triggevent, game);

        Assert.Equal([UpdateBlockers.ActName], blockers.Blocking([ActPlugin]));
        Assert.Equal([UpdateBlockers.GameName], blockers.Blocking([Dalamud]));
        Assert.Equal([UpdateBlockers.TriggeventName], blockers.Blocking([TestData.App("triggevent-jp")]));
        // 関係のないアプリは起動していても待たない
        Assert.Empty(blockers.Blocking([TestData.App()]));
    }

    [Fact]
    public async Task WaitsUntilBlockingAppsClose()
    {
        var act = new FakeGameDetector { Running = true };
        var blockers = new UpdateBlockers(act, new FakeGameDetector(), new FakeGameDetector());
        var notified = new List<string>();

        var wait = blockers.WaitAsync([ActPlugin], names => notified.AddRange(names), TimeSpan.FromMilliseconds(10), TimeSpan.FromMinutes(1),
            TimeProvider.System, CancellationToken.None);
        await Task.Delay(50);
        Assert.False(wait.IsCompleted);
        act.Running = false;

        Assert.True(await wait);
        Assert.Equal([UpdateBlockers.ActName], notified);
    }

    [Fact]
    public async Task GivesUpAfterTimeoutAndCanBeCancelled()
    {
        var blockers = new UpdateBlockers(new FakeGameDetector { Running = true }, new FakeGameDetector(), new FakeGameDetector());
        Assert.False(await blockers.WaitAsync([ActPlugin], null, TimeSpan.FromMilliseconds(5), TimeSpan.Zero, TimeProvider.System, CancellationToken.None));

        using var cts = new CancellationTokenSource();
        var wait = blockers.WaitAsync([ActPlugin], null, TimeSpan.FromMilliseconds(5), TimeSpan.FromMinutes(1), TimeProvider.System, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
