using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Decisions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Inspector;
using Avala.Workbench.Navigation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Presenting;
using Avala.Workbench.Replies;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
using Avala.Workbench.Submitting;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests;

internal sealed class Bench : IDisposable
{
    public FakeCatalog Catalog { get; } = new();

    public FakeJobs Jobs { get; } = new();

    public JobBoard Board { get; } = new();

    public FakeRunEvidence Evidence { get; } = new();

    public FakeChanges Changes { get; } = new();

    public FakeUsage Usage { get; } = new();

    public FakeAudit Audit { get; } = new();

    public FakeWorkspaces Workspaces { get; } = new();

    public FakeRecordSources Resources { get; } = new();

    public FakePermissionAnswers Permissions { get; } = new();

    public FakeAgents Agents { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero));

    public TestUiDispatcher Ui { get; } = new();

    public TestRegions Regions { get; } = new();

    public IMessenger Messenger { get; } = new StrongReferenceMessenger();

    public JobFocus Focus => new(Regions, Messenger);

    public HumanReplies Replies => new(Permissions, Agents);

    public ReviewReader Reader => new(Evidence, Catalog, Changes, Usage);

    public QueuedMessages Queue => field ??= new(Jobs);

    public ReviewDesk Desk => new(Jobs, Catalog, Changes, Queue);

    public JobInspection Inspection => new(new JobRecords(Catalog, Workspaces, Resources, Resources), new JobAudit(Audit, Audit, Usage, Audit), Board);

    public void Post(Action action) => _ = Ui.InvokeAsync(action, CancellationToken.None).AsTask();

    public BoardFeed Feed() => Feed(Ui);

    public BoardFeed Feed(IUiDispatcher ui) => new(Board, ui);

    public DecisionsViewModel Decisions() => new(Replies, Time, Feed(), Focus);

    public SidebarViewModel Sidebar() => new(Feed(), Focus);

    public NewJobViewModel NewJob { get; } = new(new JobLaunch(new SubmittingJobs(), new FakeConnections("claude-work"), new FakePreview(), new FakePolicies()), new JobBoard(), new StrongReferenceMessenger());

    public ToolbarViewModel Toolbar() => new(Decisions(), Feed(), Focus, NewJob);

    public InspectedJob Inspected() => Inspected(Ui);

    public InspectedJob Inspected(IUiDispatcher ui) => new(Feed(ui), new InspectedFacts(Inspection));

    public WorkbenchViewModel Workbench() => Workbench(Ui);

    public WorkbenchViewModel Workbench(IUiDispatcher ui) =>
        new(Feed(ui), new JobScreens(new Conversations(new JobSteering(Jobs, Board, Queue), Replies), new Reviews(Reader, Desk, ui)), Focus);

    public JobSummary Job(string instruction, JobStatus status)
    {
        var summary = Catalog.Add(instruction, status).Summary;
        var workspace = new WorkspaceId(Guid.NewGuid());
        Catalog.Change(summary.Job, history => history with { Summary = history.Summary with { Workspace = workspace } });
        Workspaces.Known[workspace] = new WorkspaceInfo(workspace, $"/worktrees/{summary.Job.Value}", "avala/fix-the-test", "ba5eba5eba5e") { BaseBranch = "main" };

        return Catalog.Summary(summary.Job);
    }

    public void Publish(params BoardJob[] jobs) =>
        Board.Publish(jobs.Aggregate(Board.Jobs, (all, job) => all.SetItem(job.Job, job)));

    public static BoardJob OnBoard(JobSummary summary, int revision = 0) =>
        new(summary, Transcript.Empty.WithPrompts(summary.Instruction, [])) { Revision = revision };

    public static ImmutableDictionary<JobId, BoardJob> Of(params BoardJob[] jobs) =>
        jobs.ToImmutableDictionary(job => job.Job);

    public void Dispose() => Ui.Dispose();
}
