using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Handoffs.Tests.Watching;

public sealed class PersonHandoffTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APersonHandsAJobToAnotherConnectionWithABriefOfItsWorkAndTheirOwnChoiceAsync()
    {
        await using var watched = new Watched();
        watched.Reading(Watched.Work, 0.4, Watched.Start.AddHours(2));
        watched.Reading(Watched.Personal, 0.2, Watched.Start.AddHours(3));

        Outcomes.Succeeds(await Handoff(watched).HandOffAsync(watched.Job, Watched.Personal, Cancellation));

        var moved = Assert.Single(watched.Jobs.Calls.Entries);
        var choice = Outcomes.Present(moved.Handoff).Choice;
        Assert.Equal(("hand off", Watched.Personal, ChoiceReason.Person), (moved.Operation, choice.Connection, choice.Reason));
        Assert.Equal([(Watched.Personal, 0.2)], choice.Compared.Select(candidate => (candidate.Connection, candidate.Used)));
        Assert.StartsWith("This job was handed off to you from work. You continue it on personal in a new conversation.", moved.Text, StringComparison.Ordinal);
        Assert.Contains(Watched.Instruction, moved.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APersonHandsAJobOnlyToAnotherUsableConnectionAsync()
    {
        await using var watched = new Watched();
        watched.Connections.Unusable.Add(Watched.Personal);
        var handoff = Handoff(watched);

        Assert.Equal(JobRejection.SameConnection, Outcomes.FailsWith(await handoff.HandOffAsync(watched.Job, Watched.Work, Cancellation)));
        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await handoff.HandOffAsync(watched.Job, Watched.Personal, Cancellation)));
        Assert.Equal(JobRejection.UnusableConnection, Outcomes.FailsWith(await handoff.HandOffAsync(watched.Job, new ConnectionName("nowhere"), Cancellation)));
        Assert.Equal(JobRejection.UnknownJob, Outcomes.FailsWith(await handoff.HandOffAsync(JobId.New(), Watched.Personal, Cancellation)));
        Assert.Empty(watched.Jobs.Calls.Entries);
    }

    private static PersonHandoff Handoff(Watched watched) => new(watched.Situations, watched.Briefs, watched.Jobs);
}
