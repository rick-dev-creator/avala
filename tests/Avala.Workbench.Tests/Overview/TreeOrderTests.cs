using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Overview;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Overview;

public sealed class TreeOrderTests
{
    private static readonly SessionId Session = SessionId.New();
    private static readonly TurnId Turn = TurnId.New();

    [Fact]
    public void ChildrenThatNeedYouComeFirstThenRunningOnesAndSettledBranchesFoldWithTheirChildren()
    {
        var running = Node("Port the idempotency keys", JobStatus.Running, 1);
        var review = Node("Regenerate the mocks", JobStatus.AwaitingReview, 1);
        var asking = Node("Rewrite webhook tests", JobStatus.NeedsHelp, 1);
        var done = Node("Write the release notes", JobStatus.Approved, 1);
        var doneChild = Node("Proofread the notes", JobStatus.Approved, 2);

        var arranged = TreeOrder.Arrange([running, done, doneChild, review, asking]);

        Assert.Equal(["Rewrite webhook tests", "Port the idempotency keys", "Regenerate the mocks"], arranged.Active.Select(node => node.Job.Instruction));
        Assert.Equal(["Write the release notes", "Proofread the notes"], arranged.Settled.Select(node => node.Job.Instruction));
    }

    [Fact]
    public void OpeningOneRowFoldsTheOneThatWasOpen()
    {
        var first = new DelegationNodeViewModel(Node("Port the idempotency keys", JobStatus.Running, 1));
        var second = new DelegationNodeViewModel(Node("Regenerate the mocks", JobStatus.Running, 1));
        DelegationNodeViewModel[] rows = [first, second];
        first.Toggled += (_, _) => TreeOrder.OneOpen(rows, first);
        second.Toggled += (_, _) => TreeOrder.OneOpen(rows, second);

        first.ToggleCommand.Execute(null);
        second.ToggleCommand.Execute(null);

        Assert.Equal((false, true), (first.IsOpen, second.IsOpen));
    }

    [Fact]
    public void ARowShowsTheContextItsSessionHasLeftItsTokensAndItsLastThreeActionsNewestFirst()
    {
        var transcript = Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), DateTimeOffset.UnixEpoch)
            .Apply(new ItemStarted(Session, Turn, new ItemId("read"), ItemKind.Search, "RefundParams"), DateTimeOffset.UnixEpoch)
            .Apply(new ItemStarted(Session, Turn, new ItemId("build"), ItemKind.Command, "go build ./internal/payments/..."), DateTimeOffset.UnixEpoch.AddSeconds(10))
            .Apply(new ItemStarted(Session, Turn, new ItemId("plan"), ItemKind.Subagent, "Subagent: Find the callers"), DateTimeOffset.UnixEpoch.AddSeconds(15))
            .Apply(new ItemStarted(Session, Turn, new ItemId("edit"), ItemKind.FileEdit, "internal/payments/refund.go"), DateTimeOffset.UnixEpoch.AddSeconds(20))
            .Apply(new ItemStarted(Session, Turn, new ItemId("test"), ItemKind.Command, "go test ./internal/payments/..."), DateTimeOffset.UnixEpoch.AddSeconds(30))
            .Apply(new ContextReported(Session, Turn, 56_000, 200_000) { Compacted = true }, DateTimeOffset.UnixEpoch.AddSeconds(31));
        var node = Node("Update call sites", JobStatus.Running, 1, transcript, new TokenUsage(90_000, 6_000, 400, 0, 0));

        var row = new DelegationNodeViewModel(node);

        Assert.Equal((true, "72%", true), (row.Context.IsShown, row.Context.Left, row.Context.Compacted));
        Assert.Equal("96.4k tokens", row.Tokens);
        Assert.Equal(["Run go test ./internal/payments/...", "Edit internal/payments/refund.go", "Run go build ./internal/payments/..."], row.Recent.Select(action => action.Text));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(30), row.Recent[0].Since);
        Assert.Equal(("Run go test ./internal/payments/...", 1), (row.Doing, row.SubagentCount));
    }

    [Fact]
    public void TheOrchestratorTalliesItsChildrenThatNeedYouRunAreDoneAndWereRefusedAndSumsTheTreesTokens()
    {
        var root = Pages.Summary("Migrate payments", JobStatus.Running);
        var refused = new DelegationRecord(Session, new ItemId("call"), "Rewrite in Svelte", DateTimeOffset.UnixEpoch) { Parent = root.Job, Depth = 1, Refusal = DelegationError.Malformed };
        var tree = new DelegationTree(
            root,
            [
                Node("Rewrite webhook tests", JobStatus.NeedsHelp, 1, tokens: new TokenUsage(1_000, 0, 0, 0, 0)),
                Node("Port the idempotency keys", JobStatus.Running, 1, tokens: new TokenUsage(2_000, 0, 0, 0, 0)),
                Node("Update the scheduler", JobStatus.Running, 1),
                Node("Write the notes", JobStatus.Approved, 1),
            ],
            [refused]);

        var card = new OrchestratorCardViewModel(tree);

        Assert.Equal(["1 needs you", "2 running", "1 done", "1 refused"], card.Tally.Select(tally => tally.Text));
        Assert.Equal(("3k tokens", false), (card.Tokens, card.Context.IsShown));
    }

    [Fact]
    public void ARowWhoseHarnessReportedNoContextShowsNone() =>
        Assert.False(new DelegationNodeViewModel(Node("Update call sites", JobStatus.Running, 1)).Context.IsShown);

    private static DelegationNode Node(string instruction, JobStatus status, int depth, Transcript? transcript = null, TokenUsage tokens = default)
    {
        var summary = Pages.Summary(instruction, status);
        var usage = tokens == default ? Option<UsageSummary>.None : new UsageSummary(tokens, [], 0, default, []);

        return new DelegationNode(summary, depth, Option<JobFact>.None, Option<DelegationRecord>.None, new JobSpend(Option<SessionSeen>.None, usage, Option<Budgets.Contracts.BudgetCaps>.None, Option<Budgets.Contracts.BudgetCarve>.None))
        {
            Seen = new BoardJob(summary, transcript ?? Transcript.Empty),
        };
    }
}
