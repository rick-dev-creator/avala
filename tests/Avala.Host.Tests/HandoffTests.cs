using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class HandoffTests(PublishedPlugins plugins)
{
    private const string SameHarness = """{ "limits": { "onLimit": "handoff-same-harness" } }""";

    private const string AnyHarness = """{ "limits": { "onLimit": "handoff-any-harness" } }""";

    private const string ResetMessage = "The usage limit window has reset. Go on where you left off.";

    private static readonly (string File, string Content)[] TwoAccounts =
    [
        ("simulated-logins/one/.login", string.Empty),
        ("simulated-logins/two/.login", string.Empty),
    ];

    private static readonly (string File, string Content)[] TwoHarnesses =
    [
        ("connections.json", """
            {
              "connections": [
                { "name": "one", "provider": "simulator" },
                { "name": "other", "provider": "simulator-second" }
              ]
            }
            """),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ConnectionName One => new("simulator-one");

    private static ConnectionName Two => new("simulator-two");

    private static string Instruction => SimulatedRun.Simulate("limit-handoff");

    [Fact]
    public async Task AJobAtItsLimitWhenItsTurnEndsContinuesOnTheOtherAccountWithABriefOfItsOwnRecordsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(SameHarness));
        var recorded = run.Watch<HandoffRecorded>();
        var told = run.Watch<AgentActivity>();
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        var brief = await BriefAsync(told);
        var handoff = (await recorded.UntilAsync(announced => announced.Handoff.Job == job)).Handoff;
        await ReviewedAsync(progress, job);

        Assert.Equal((One, Two, 2), (handoff.From, handoff.To, handoff.Attempt));
        Assert.Equal(new LimitReason(One, "5h", 0.95, 0.9), Outcomes.Present(handoff.Why));
        Assert.Equal([new Cost(0.0600m, "USD")], handoff.Spent);
        Assert.StartsWith("This job was handed off to you from simulator-one, which reached 95% of its 5-hour window. You continue it on simulator-two in a new conversation.", brief, StringComparison.Ordinal);
        Assert.All(
            [$"## The original instruction\n{Instruction}", "- calculator.txt (added, +1 -0)", "- calculator: Failed, exit 1", "Feedback: ", "- Make the failing checks pass: calculator.", "The calculator is ready, though the usage window is nearly spent."],
            expected => Assert.Contains(expected.ReplaceLineEndings(), brief, StringComparison.Ordinal));
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        Assert.Equal(Option<ConnectionName>.Some(Two), history.Summary.Connection);
        Assert.Equal(
            [(AttemptOrigin.Initial, AttemptOutcome.Rejected), (AttemptOrigin.Handoff, AttemptOutcome.Rejected), (AttemptOrigin.Retry, AttemptOutcome.Passed)],
            history.Attempts.Select(attempt => (attempt.Origin, attempt.Outcome)));
        Assert.Equal(Option<string>.Some(brief), history.Attempts[1].Guidance);
        Assert.Equal((Two, ChoiceReason.MostCapacity), (Outcomes.Present(history.Choice).Connection, Outcomes.Present(history.Choice).Reason));
        Assert.Equal([handoff], run.Get<IHandoffs>().OfJob(job));
    }

    [Fact]
    public async Task AJobAtItsLimitContinuesOnAnotherHarnessWhenAnyHarnessMayTakeItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoHarnesses, Committed(AnyHarness));
        var opened = run.Watch<SessionOpened>();
        var told = run.Watch<AgentActivity>();
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        var brief = await BriefAsync(told);
        var other = await opened.UntilAsync(session => session.Connection == new ConnectionName("other"));
        await ReviewedAsync(progress, job);

        Assert.Equal("simulator-second", other.Provider.Id);
        Assert.StartsWith("This job was handed off to you from one, which reached 95% of its 5-hour window. You continue it on other in a new conversation.", brief, StringComparison.Ordinal);
        Assert.Equal(Option<ConnectionName>.Some(new ConnectionName("other")), Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Connection);
    }

    [Fact]
    public async Task AThresholdCrossedMidTurnHandsTheJobOffOnlyOnceItsTurnHasEndedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(SameHarness));
        var activity = run.Watch<AgentActivity>();
        var handed = run.Watch<JobHandedOff>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        var first = (await activity.CollectUntilAsync(update => update.Event is TurnCompleted)).ToList();
        var moved = await handed.UntilAsync(announced => announced.Job == job);

        var reading = first.FindIndex(update => update.Event is LimitReported);
        Assert.True(reading >= 0, "The turn reported no limit");
        Assert.Contains(first.Skip(reading), update => update.Event is ItemProgressed { Item.Value: "reply", Text: "though the usage window is nearly spent." });
        Assert.Equal(TurnOutcome.Finished, ((TurnCompleted)first[^1].Event).Outcome);
        Assert.NotEqual(first[^1].Event.Session, moved.Session);
        var attempts = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Attempts;
        Assert.Equal((AttemptOrigin.Initial, AttemptOutcome.Rejected), (attempts[0].Origin, attempts[0].Outcome));
        Assert.Equal(AttemptOrigin.Handoff, attempts[1].Origin);
    }

    [Fact]
    public async Task AConnectionNamedOnTheJobIsNeverSwitchedAndItsJobResumesInItsConversationWhenTheWindowResetsAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(SameHarness));
        var held = run.Watch<JobHeld>();
        var waits = run.Watch<JobWaitsForReset>();
        var resumed = run.Watch<JobResumedAtReset>();
        var progress = run.Watch<JobProgressed>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction) { Connection = One }));
        var hold = (await held.UntilAsync(announced => announced.Hold.Job == job)).Hold;
        var wait = (await waits.UntilAsync(announced => announced.Wait.Job == job)).Wait;
        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        await workbench.ShowsInGroupAsync(job, "NeedsYou");
        var pill = await run.Ui.ReadAsync(() => conversation["Pill"]["Text"].Text);
        run.AdvanceTo(Outcomes.Present(wait.ResumesAt));
        var back = await resumed.UntilAsync(announced => announced.Job == job);
        await ReviewedAsync(progress, job);

        Assert.Equal((HoldReason.LimitNearlyReached, SessionHalt.Idle), (hold.Reason, hold.Halt));
        Assert.Equal((One, "5h"), (wait.Connection, wait.Window));
        Assert.Equal($"Held · resumes at {Outcomes.Present(wait.ResumesAt).ToLocalTime():HH:mm} when the 5-hour window resets", pill);
        Assert.Equal((One, ContinuedIn.SameSession), (back.Connection, back.Conversation));
        var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation));
        Assert.Equal(Option<ConnectionName>.Some(One), history.Summary.Connection);
        Assert.StartsWith($"{ResetMessage}\n\n", Outcomes.Present(history.Attempts[^1].Guidance), StringComparison.Ordinal);
        Assert.Empty(run.Get<IHandoffs>().OfJob(job));
        Assert.Equal(Option<ResetWait>.None, run.Get<IHandoffs>().WaitOf(job));
    }

    [Fact]
    public async Task WithNoCapacityAnywhereTheJobWaitsForTheEarliestResetAndContinuesWhereItCameFirstAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, [(".avala/jobs.json", SameHarness)]);
        var progress = run.Watch<JobProgressed>();
        var spent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("spent-elsewhere")) { Connection = Two }));
        await ReviewedAsync(progress, spent);
        await run.Repository.CommitAsync(".avala/checks.json", ReviewTests.CalculatorChecks, Cancellation);
        await run.Repository.CommitAsync(".avala/permissions.json", ReviewTests.TestsPolicy, Cancellation);
        var waits = run.Watch<JobWaitsForReset>();
        var recorded = run.Watch<HandoffRecorded>();

        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        var wait = (await waits.UntilAsync(announced => announced.Wait.Job == job)).Wait;
        run.AdvanceTo(Outcomes.Present(wait.ResumesAt));
        var handoff = (await recorded.UntilAsync(announced => announced.Handoff.Job == job)).Handoff;
        await ReviewedAsync(progress, job);

        Assert.Equal((Two, "5h"), (wait.Connection, wait.Window));
        Assert.True(Outcomes.Present(wait.ResumesAt) < wait.Since.AddMinutes(31), $"Resumes at {wait.ResumesAt}, waiting since {wait.Since}");
        Assert.Equal((One, Two), (handoff.From, handoff.To));
    }

    [Fact]
    public async Task AJobWaitingForAResetResumesItsConversationAfterARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(SameHarness));
        var waits = run.Watch<JobWaitsForReset>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction) { Connection = One }));
        var wait = (await waits.UntilAsync(announced => announced.Wait.Job == job)).Wait;

        await run.RestartAsync();
        await run.StartedAsync();
        var restored = run.Get<IHandoffs>().WaitOf(job);
        var resumed = run.Watch<JobResumedAtReset>();
        var progress = run.Watch<JobProgressed>();
        run.AdvanceTo(Outcomes.Present(wait.ResumesAt));
        var back = await resumed.UntilAsync(announced => announced.Job == job);
        await ReviewedAsync(progress, job);

        Assert.Equal((wait.Connection, wait.Window, wait.ResumesAt), restored.Match(found => (found.Connection, found.Window, found.ResumesAt), () => default));
        Assert.Equal(ContinuedIn.ResumedConversation, back.Conversation);
    }

    [Fact]
    public async Task TheInspectorAndTheConversationSayWhereTheJobWasHandedOffAndWhatItSpentOnEachAccountAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, TwoAccounts, Committed(SameHarness));
        var progress = run.Watch<JobProgressed>();
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        await ReviewedAsync(progress, job);

        var workbench = await run.WorkbenchAsync();
        var conversation = await workbench.SelectAsync(job);
        await workbench.ShowsInGroupAsync(job, "ReadyForReview");
        var section = Assert.Single(await workbench.InspectAsync(job, "AutonomySectionViewModel"));
        var (origins, lines) = await run.Ui.ReadAsync(() => (
            OpenWorkbench.Texts(conversation, "PromptViewModel", "Origin"),
            section["Handoffs"].Items.Select(line => (line["Moved"].Text, line["Spent"].Text)).ToList()));

        Assert.Contains("Handed off to simulator-two at 95% of the 5-hour window", origins);
        Assert.StartsWith("Handed off from simulator-one to simulator-two at 95% of the 5-hour window · ", lines[0].Item1, StringComparison.Ordinal);
        Assert.Equal("Spent on simulator-one: 0.06 USD · 15,275 tokens", lines[0].Item2);
        Assert.StartsWith("Spent on simulator-two: ", lines[1].Item2, StringComparison.Ordinal);

        await run.RestartAsync();
        await run.StartedAsync();
        Assert.Single(run.Get<IHandoffs>().OfJob(job));
    }

    private static (string Path, string Content)[] Committed(string limits) =>
    [
        (".avala/checks.json", ReviewTests.CalculatorChecks),
        (".avala/permissions.json", ReviewTests.TestsPolicy),
        (".avala/jobs.json", limits),
    ];

    private static async Task<string> BriefAsync(EventWatch<AgentActivity> activity)
    {
        var told = await activity.UntilAsync(update => update.Event is ItemProgressed { Item.Value: "told" } progressed && progressed.Text.Contains("handed off", StringComparison.Ordinal));

        return ((ItemProgressed)told.Event).Text;
    }

    private static async Task ReviewedAsync(EventWatch<JobProgressed> progress, JobId job) =>
        _ = await progress.UntilAsync(update => update.Job == job && update.Status == JobStatus.AwaitingReview);
}
