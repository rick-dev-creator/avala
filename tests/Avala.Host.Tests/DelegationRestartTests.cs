using System.Text.RegularExpressions;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class DelegationRestartTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private static readonly (string Path, string Content)[] Delegating =
    [
        (".avala/checks.json", PassingChecks),
        (".avala/jobs.json", """{ "delegation": {} }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AChildRunningAtARestartResumesAndItsWaitingParentIsToldItsReportOnceInItsResumedConversationAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Delegating);
        var (parent, child) = await DelegatedAsync(run, "delegate-paused");

        await run.RestartAsync();

        var reported = await run.ChildReportedAsync();
        var delivered = await run.ReportDeliveredAsync();
        var told = await run.ToldAsync();
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(parent));
        Assert.Equal((Option<JobId>.Some(child), ChildOutcome.Integrated), (reported.Child, Outcomes.Present(reported.Report).Outcome));
        Assert.True(reported.Answered.IsNone, "The parent's call died with its session, so the report cannot have reached it as a tool result");
        Assert.Equal((Option<JobId>.Some(child), AnswerRoute.Message), (delivered.Child, Outcomes.Present(delivered.Answered).Route));
        Assert.StartsWith("The harness restarted while you were working on this job.", told, StringComparison.Ordinal);
        Assert.Equal(1, Mentions(told, child));
        Assert.True(File.Exists(Path.Combine(await WorktreeAsync(run, parent), "NOTES.md")));
        await AssertShownAsync(run, parent, "Integrated · told to its parent in a message");
        await AssertIdempotentAfterAnotherRestartAsync(run, parent, child);
    }

    [Fact]
    public async Task AChildWaitingForAPersonAcrossTwoRestartsAsksAgainAndItsParentIsToldItsReportOnceAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Delegating);
        var (parent, child) = await DelegatedAsync(run, "delegate-waiting");
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);

        await run.RestartAsync();
        Assert.Equal(DecisionDelivery.LeftToHuman, (await run.DecisionAsync()).Delivery);
        await run.RestartAsync();
        var asked = await run.DecisionAsync();
        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));

        var delivered = await run.ReportDeliveredAsync();
        var told = await run.ToldAsync();
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(parent));
        Assert.Equal((Option<JobId>.Some(child), AnswerRoute.Message), (delivered.Child, Outcomes.Present(delivered.Answered).Route));
        Assert.Equal(1, Mentions(told, child));
        await AssertShownAsync(run, parent, "Integrated · told to its parent in a message");
        await AssertIdempotentAfterAnotherRestartAsync(run, parent, child);
    }

    [Fact]
    public async Task AChildThatFinishedWhileItsParentWasHeldIsToldToTheParentOnceWhenAPersonContinuesItAfterARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, Delegating);
        var (parent, child) = await DelegatedAsync(run, "delegate-waiting");
        var asked = await run.DecisionAsync();
        Outcomes.Succeeds(await run.Get<IJobs>().HoldAsync(parent, HoldReason.Interrupted, Cancellation));
        Outcomes.Succeeds(await run.Get<IAgents>().RespondAsync(asked.Session, new PermissionDecision(asked.Item, PermissionAnswer.Allow), Cancellation));
        var reported = await run.ChildReportedAsync();
        Assert.True(reported.Answered.IsNone, "The held parent's call was abandoned, so the report cannot have reached it");

        await run.RestartAsync();
        await run.StartedAsync();
        await AssertShownAsync(run, parent, "Integrated · not yet told to its parent", "NeedsYou");
        var continued = Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(parent, "Carry on with the release.", Cancellation));

        var delivered = await run.ReportDeliveredAsync();
        var told = await run.ToldAsync();
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(parent));
        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.Equal((Option<JobId>.Some(child), AnswerRoute.Message), (delivered.Child, Outcomes.Present(delivered.Answered).Route));
        Assert.StartsWith("Carry on with the release.", told, StringComparison.Ordinal);
        Assert.Equal(1, Mentions(told, child));
        await AssertIdempotentAfterAnotherRestartAsync(run, parent, child);
        await AssertShownAsync(run, parent, "Integrated · told to its parent in a message");
    }

    [Fact]
    public async Task AParentWhoseHarnessCannotResumeIsHeldAfterARestartWithItsChildsReportShownAndNotYetToldAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [
                ("connections.json", """
                    {
                      "default": "sim",
                      "connections": [
                        { "name": "sim", "provider": "simulator", "credential": { "source": "login" } },
                        { "name": "plain", "provider": "simulator", "credential": { "source": "login" }, "settings": { "withoutCapabilities": "resumable" } }
                      ]
                    }
                    """),
                ("connections/sim/.login", string.Empty),
                ("connections/plain/.login", string.Empty),
            ],
            [(".avala/checks.json", PassingChecks), (".avala/jobs.json", """{ "delegation": { "connections": ["sim"] } }""")]);
        var delegated = run.Watch<ChildDelegated>();
        var resumable = run.Watch<JobResumable>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(
            new JobRequest(string.Empty, SimulatedRun.Simulate("delegate-paused")) { Connection = new ConnectionName("plain") }));
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        _ = await resumable.UntilAsync(update => update.Job == child);

        await run.RestartAsync();

        var hold = await run.HoldAsync();
        var reported = await run.ChildReportedAsync();
        Assert.Equal((parent, HoldReason.NotResumable), (hold.Job, hold.Reason));
        Assert.Equal(ChildOutcome.Integrated, Outcomes.Present(reported.Report).Outcome);
        Assert.True(Outcomes.Present(run.Get<IDelegations>().OfChild(child)).Answered.IsNone);
        await AssertShownAsync(run, parent, "Integrated · not yet told to its parent", "NeedsYou");
        var workbench = await run.WorkbenchAsync();
        Assert.Equal(("NeedsYou", "held: its conversation cannot resume"), await workbench.RowAsync(parent, "NeedsYou"));
    }

    private static async Task<(JobId Parent, JobId Child)> DelegatedAsync(SimulatedRun run, string scenario)
    {
        var delegated = run.Watch<ChildDelegated>();
        var resumable = run.Watch<JobResumable>();
        var parent = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate(scenario))));
        var child = Outcomes.Present((await delegated.UntilAsync(_ => true)).Delegation.Child);
        var seen = new HashSet<JobId>();
        _ = await resumable.UntilAsync(update => seen.Add(update.Job) && seen.IsSupersetOf([parent, child]));

        return (parent, child);
    }

    private static async Task AssertIdempotentAfterAnotherRestartAsync(SimulatedRun run, JobId parent, JobId child)
    {
        var record = Delivery(Outcomes.Present(run.Get<IDelegations>().OfChild(child)));
        var branch = await BranchAsync(run, parent);

        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Equal(record, Delivery(Outcomes.Present(run.Get<IDelegations>().OfChild(child))));
        Assert.Equal([child], (await run.Get<IJobCatalog>().ChildrenAsync(parent, Cancellation)).Select(summary => summary.Job));
        Assert.Equal(JobStatus.AwaitingReview, Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(parent, Cancellation)).Summary.Status);
        var log = await run.Repository.GitAsync(Cancellation, "log", "--format=%B", branch);
        Assert.True(Regex.Count(log, $"Avala-Job: {child.Value}") <= 1, $"The child was integrated more than once:\n{log}");
        Assert.Single(await run.Get<IJobCatalog>().ListAsync(Cancellation), summary => summary.Parent == Option<JobId>.Some(parent));
    }

    private static async Task AssertShownAsync(SimulatedRun run, JobId parent, string inspected, string group = "ReadyForReview")
    {
        var overview = run.Page("Overview");
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)overview.Target).Activate();

            return overview.Has("Loading") ? overview["Loading"].Value<Task>() : Task.CompletedTask;
        });
        var delegation = overview["Delegation"];
        await run.Ui.PresentedAsync(
            delegation.Presentation,
            () => delegation["Children"].Items.Count == 1 && delegation["Children"].Items[0]["Activity"].Text == "integrated into its parent",
            () => string.Join(", ", delegation["Children"].Items.Select(item => item["Activity"].Text)));
        var workbench = await run.WorkbenchAsync();
        await workbench.ShowsInGroupAsync(parent, group);
        var section = Assert.Single(await workbench.InspectAsync(parent, "DelegationSectionViewModel"));
        await run.Ui.PresentedAsync(
            section.Presentation,
            () => section["Children"].Value<IReadOnlyList<string>>() is [var line] && line.EndsWith(inspected, StringComparison.Ordinal),
            () => string.Join(" | ", section["Children"].Value<IReadOnlyList<string>>()));
    }

    private static (Option<CallAnswer> Answered, Option<ChildOutcome> Outcome, Option<DateTimeOffset> At) Delivery(DelegationRecord record) =>
        (record.Answered, record.Report.Map(report => report.Outcome), record.Report.Map(report => report.At));


    private static int Mentions(string told, JobId child) => Regex.Count(told, $"\"job\":\"{child.Value}\"");

    private static async Task<string> WorktreeAsync(SimulatedRun run, JobId job) => (await WorkspaceAsync(run, job)).Path;

    private static async Task<string> BranchAsync(SimulatedRun run, JobId job) => (await WorkspaceAsync(run, job)).Branch;

    private static async Task<WorkspaceInfo> WorkspaceAsync(SimulatedRun run, JobId job)
    {
        var workspace = Outcomes.Present(Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, Cancellation)).Summary.Workspace);

        return Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAsync(workspace, Cancellation));
    }
}
