using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;

namespace Avala.Handoffs.Tests.Watching;

public sealed class CompactionTests
{
    private static readonly LimitRules Auto = LimitRules.Default with { Resume = ResumeAtReset.Auto };

    [Fact]
    public async Task AtTheResetAJobWhoseSessionIsStillOpenIsCompactedBeforeItIsToldToGoOnAsync()
    {
        await using var watched = Waiting(Auto);
        await WaitedAsync(watched);

        watched.Clock.Advance(TimeSpan.FromHours(1));
        var moved = await watched.MovedAsync();

        Assert.Equal([(watched.Session, "/compact")], watched.Agents.Said.Entries);
        Assert.Equal(("continue", LimitActions.ResetMessage), (moved.Operation, moved.Text));
        Assert.Equal(
            [RelayMarkKind.Waiting, RelayMarkKind.Compacting, RelayMarkKind.Resumed],
            watched.Book.MarksOf(watched.Job).Select(mark => mark.Kind));
    }

    [Fact]
    public async Task RulesThatDoNotCompactTellTheJobToGoOnWithoutCompactingAsync()
    {
        await using var watched = Waiting(Auto with { CompactOnResume = false });
        await WaitedAsync(watched);

        watched.Clock.Advance(TimeSpan.FromHours(1));
        _ = await watched.MovedAsync();

        Assert.Empty(watched.Agents.Said.Entries);
    }

    [Fact]
    public async Task AJobWhoseSessionWasClosedGoesOnWithoutCompactingAsync()
    {
        await using var watched = Waiting(Auto);
        watched.Agents.Open.Clear();
        await WaitedAsync(watched);

        watched.Clock.Advance(TimeSpan.FromHours(1));
        _ = await watched.MovedAsync();

        Assert.Empty(watched.Agents.Said.Entries);
    }

    [Fact]
    public async Task AHarnessThatCannotCompactOnRequestGoesOnWithoutCompactingAsync()
    {
        await using var watched = Waiting(Auto);
        watched.Connections.Compacts = false;
        await WaitedAsync(watched);

        watched.Clock.Advance(TimeSpan.FromHours(1));
        _ = await watched.MovedAsync();

        Assert.Empty(watched.Agents.Said.Entries);
    }

    [Fact]
    public async Task ACompactionThatNeverFinishesStillLetsTheJobGoOnAfterAWhileAsync()
    {
        await using var watched = Waiting(Auto);
        watched.Agents.Answer = (_, _) => Task.CompletedTask;
        await WaitedAsync(watched);

        watched.Clock.Advance(TimeSpan.FromHours(1));

        _ = await watched.Clock.Timers.WaitForAsync(due => due == Compaction.Patience);
        watched.Clock.Advance(Compaction.Patience);
        var moved = await watched.MovedAsync();

        Assert.Equal("continue", moved.Operation);
    }

    private static Watched Waiting(LimitRules rules)
    {
        var watched = new Watched();
        watched.Rules.Rules = rules;
        watched.Connections.Compacts = true;
        watched.Agents.Open.Add(watched.Session);
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(1));

        return watched;
    }

    private static async Task WaitedAsync(Watched watched)
    {
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        _ = await watched.WaitingAsync();
    }
}
