using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class RelayTests(PublishedPlugins plugins)
{
    private const string WindingDown = """{ "limits": { "onLimit": "handoff-same-harness" } }""";

    private const string CappedAtOnce = """{ "limits": { "onLimit": "handoff-same-harness", "hardCap": 0.92 } }""";

    private const string InOrder = """{ "limits": { "onLimit": "handoff-same-harness", "strategy": "in-order", "connections": ["simulator-one", "simulator-two", "simulator-three"] } }""";

    private static readonly (string File, string Content)[] TwoAccounts =
    [
        ("simulated-logins/one/.login", string.Empty),
        ("simulated-logins/two/.login", string.Empty),
    ];

    private static readonly (string File, string Content)[] ThreeAccounts =
    [
        .. TwoAccounts,
        ("simulated-logins/three/.login", string.Empty),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ConnectionName One => new("simulator-one");

    private static ConnectionName Two => new("simulator-two");

    private static ConnectionName Three => new("simulator-three");

    [Fact]
    public async Task AJobPastItsThresholdIsAskedToWindDownMidTurnThenContinuesOnTheOtherAccountAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(WindingDown));
        var marked = run.Watch<RelayMarked>();
        var activity = run.Watch<AgentActivity>();
        var recorded = run.Watch<HandoffRecorded>();
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("wind-down"))));
        var mark = (await marked.UntilAsync(announced => announced.Mark.Job == job)).Mark;
        var steered = (MessageQueued)(await activity.UntilAsync(update => update.Event is MessageQueued queued && queued.Text.StartsWith("Avala note: the 5h usage window", StringComparison.Ordinal))).Event;
        var handoff = (await recorded.UntilAsync(announced => announced.Handoff.Job == job)).Handoff;
        await ReviewedAsync(progress, job);

        Assert.Equal((RelayMarkKind.WindingDown, One), (mark.Kind, mark.Connection));
        Assert.Equal(new LimitReason(One, "5h", 0.92, 0.9), Outcomes.Present(mark.Why));
        Assert.Contains("leave the work in a valid state", steered.Text, StringComparison.Ordinal);
        Assert.Equal((One, Two), (handoff.From, handoff.To));
        Assert.Equal([RelayMarkKind.WindingDown], run.Get<IHandoffs>().MarksOf(job).Select(kept => kept.Kind));
    }

    [Fact]
    public async Task AWoundDownTurnWhoseChecksFailRelaysWithTheirEvidenceInTheBriefAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(WindingDown));
        var recorded = run.Watch<HandoffRecorded>();
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("wind-down-broken"))));
        var handoff = (await recorded.UntilAsync(announced => announced.Handoff.Job == job)).Handoff;
        await ReviewedAsync(progress, job);

        var brief = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Attempts[1].Guidance);
        Assert.Equal((One, Two), (handoff.From, handoff.To));
        Assert.Contains("- calculator: failed, exit 1", brief, StringComparison.Ordinal);
        Assert.Contains("- Make the failing checks pass: calculator.", brief, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJobThatReachesItsHardCapIsStoppedMidTurnAndRelayedWithoutWindingDownAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(CappedAtOnce));
        var marked = run.Watch<RelayMarked>();
        var recorded = run.Watch<HandoffRecorded>();
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("wind-down"))));
        var mark = (await marked.UntilAsync(announced => announced.Mark.Job == job)).Mark;
        var handoff = (await recorded.UntilAsync(announced => announced.Handoff.Job == job)).Handoff;
        await ReviewedAsync(progress, job);

        Assert.Equal((RelayMarkKind.Capped, One), (mark.Kind, mark.Connection));
        Assert.Equal((One, Two), (handoff.From, handoff.To));
        Assert.DoesNotContain(run.Get<IHandoffs>().MarksOf(job), kept => kept.Kind == RelayMarkKind.WindingDown);
    }

    [Fact]
    public async Task AJobRelaysAcrossThreeAccountsInOrderAndFinishesOnTheLastAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, ThreeAccounts, Committed(InOrder));
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("relay-chain"))));
        await ReviewedAsync(progress, job);

        var handoffs = run.Get<IHandoffs>().OfJob(job);
        Assert.Equal([(One, Two), (Two, Three)], handoffs.Select(handoff => (handoff.From, handoff.To)));
        Assert.Equal([(RelayMarkKind.WindingDown, One), (RelayMarkKind.WindingDown, Two)], run.Get<IHandoffs>().MarksOf(job).Select(kept => (kept.Kind, kept.Connection)));
        Assert.Equal(Option<ConnectionName>.Some(Three), Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Connection);
    }

    private static (string Path, string Content)[] Committed(string limits) =>
    [
        (".avala/checks.json", ReviewTests.CalculatorChecks),
        (".avala/permissions.json", ReviewTests.TestsPolicy),
        (".avala/jobs.json", limits),
    ];

    private static async Task ReviewedAsync(EventWatch<JobProgressed> progress, JobId job) =>
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.AwaitingReview);
}
