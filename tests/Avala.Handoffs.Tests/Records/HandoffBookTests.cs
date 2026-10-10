using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Tests.Watching;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Tests.Records;

public sealed class HandoffBookTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AHandoffRecordsWhatTheJobSpentOnTheConnectionItLeftPerCurrencyAsync()
    {
        await using var watched = new Watched();
        Spent(watched, watched.Job, Watched.Work, new TokenUsage(100, 20, 10, 5, 1), new Cost(0.10m, "USD"));
        Spent(watched, watched.Job, Watched.Work, new TokenUsage(200, 40, 0, 0, 0), new Cost(0.05m, "USD"), new Cost(0.30m, "EUR"));
        Spent(watched, watched.Job, Watched.Personal, new TokenUsage(9_000, 0, 0, 0, 0), new Cost(9m, "USD"));
        Spent(watched, JobId.New(), Watched.Work, new TokenUsage(9_000, 0, 0, 0, 0), new Cost(9m, "USD"));
        watched.Usage.Opened.Add(new UsageSession(SessionId.New(), Watched.Agent, Option<AgentAccount>.None, Watched.Work, Watched.Start) { Job = watched.Job });
        var choice = new ConnectionChoice(Watched.Personal, ChoiceReason.MostCapacity, [], Watched.Start);

        await watched.Book.HandleAsync(new JobHandedOff(watched.Job, Watched.Work, Watched.Personal, SessionId.New(), 2, choice), Cancellation);

        var handoff = Assert.Single(watched.Book.OfJob(watched.Job));
        Assert.Equal([new Cost(0.15m, "USD"), new Cost(0.30m, "EUR")], handoff.Spent);
        Assert.Equal(376, handoff.Tokens);
        Assert.Equal([handoff], watched.Store.Handoffs);
        Assert.Equal(new HandoffRecorded(handoff), Assert.Single(watched.Bus.Published));
    }

    private static void Spent(Watched watched, JobId job, Avala.Agents.Contracts.Connections.ConnectionName connection, TokenUsage tokens, params Cost[] costs)
    {
        var session = SessionId.New();
        watched.Usage.Opened.Add(new UsageSession(session, Watched.Agent, Option<AgentAccount>.None, connection, Watched.Start) { Job = job });
        watched.Usage.Spent[session] = new UsageSummary(tokens, costs, 0, default, []);
    }
}
