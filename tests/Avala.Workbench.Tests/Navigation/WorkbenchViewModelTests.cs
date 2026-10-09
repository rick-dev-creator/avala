using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Replies;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Navigation;

public sealed class WorkbenchViewModelTests : IDisposable
{
    private readonly JobBoard board = new();
    private readonly TestUiDispatcher ui = new();
    private readonly WorkbenchViewModel workbench;

    public WorkbenchViewModelTests() =>
        workbench = new WorkbenchViewModel(
            board,
            ui,
            new SidebarViewModel(),
            new Conversations(new JobSteering(new FakeJobs(), board), new HumanReplies(new FakePermissionAnswers(), new FakeAgents())));

    [Fact]
    public async Task WhileActiveTheSidebarFollowsTheBoardAndSelectingAJobOpensItsConversation()
    {
        var summary = new FakeCatalog().Add("Fix the failing test", JobStatus.Running).Summary;
        workbench.Activate();

        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty.WithPrompts(summary.Instruction, []))));
        await ui.UntilAsync(() => workbench.Sidebar.Running.Count == 1);
        await ui.InvokeAsync(() => workbench.Sidebar.SelectCommand.Execute(workbench.Sidebar.Running[0]), TestContext.Current.CancellationToken);

        var conversation = await ui.ReadAsync(() => workbench.Conversation);
        Assert.Equal((summary.Job, "Fix the failing test"), (conversation?.Job, conversation?.Title));
        Assert.IsType<PromptViewModel>(Assert.Single(conversation!.Entries));
    }

    [Fact]
    public async Task AfterDeactivationTheBoardIsNoLongerFollowed()
    {
        var summary = new FakeCatalog().Add("Fix the failing test", JobStatus.Running).Summary;
        workbench.Activate();
        workbench.Deactivate();
        await workbench.Following;

        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(summary.Job, new BoardJob(summary, Transcript.Empty)));

        Assert.Empty(await ui.ReadAsync(() => workbench.Sidebar.Running));
    }

    [Fact]
    public void TheInspectorIsClosedUntilToggled()
    {
        var closed = workbench.IsInspectorOpen;

        workbench.ToggleInspectorCommand.Execute(null);

        Assert.Equal((false, true), (closed, workbench.IsInspectorOpen));
    }

    public void Dispose()
    {
        workbench.Dispose();
        ui.Dispose();
    }
}
