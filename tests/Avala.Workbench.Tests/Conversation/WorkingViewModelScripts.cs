using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class WorkingViewModelScripts
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();

    [Fact]
    public void BetweenItemsARowAtTheLiveEdgeSaysTheAgentThinksAndSinceWhen()
    {
        var running = Job().WithPrompts("Fix it", []).Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("test"), ItemKind.Command, "Run npm test") { Input = "npm test" }, Now.AddSeconds(3));

        ViewModelScript.Given(new WorkingViewModel())
            .When(working => working.Show(Board(running)))
            .Then(working => Assert.False(working.IsShown))
            .When(working => working.Show(Board(running.Apply(new ItemCompleted(session, turn, new ItemId("test"), ItemOutcome.Succeeded), Now.AddSeconds(9)))))
            .Then(working => Assert.Equal((true, "Thinking", true, Now), (working.IsShown, working.Verb, working.ShowsElapsed, working.Since)));
    }

    [Fact]
    public void TheWorkingRowWaitsForYouWhileARequestIsYours()
    {
        var asked = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("migrate"), ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, Now));

        ViewModelScript.Given(new WorkingViewModel())
            .When(working => working.Show(Board(asked)))
            .Then(working => Assert.Equal((true, true, "Waiting for you"), (working.IsShown, working.IsWaiting, working.Verb)));
    }

    [Theory]
    [InlineData(JobStatus.Preparing, "Starting")]
    [InlineData(JobStatus.Checking, "Running the checks")]
    public void BeforeAndAfterItsTurnsTheWorkingRowSaysWhatTheJobDoes(JobStatus status, string verb)
    {
        var working = new WorkingViewModel();

        working.Show(Board(Job().WithPrompts("Fix it", []), status));

        Assert.Equal((true, verb, false), (working.IsShown, working.Verb, working.ShowsElapsed));
    }

    [Fact]
    public void WhileAvalaFindsTheChecksTheWorkingRowSaysSoInsteadOfRunningThem()
    {
        var working = new WorkingViewModel();

        working.Show(Board(Job().WithPrompts("Fix it", []), JobStatus.Checking) with { FindsChecks = true });

        Assert.Equal((true, "Finding how to check the work"), (working.IsShown, working.Verb));
    }

    [Fact]
    public void TheWorkingRowStepsAsideWhileAnItemIsOpenAndOnceTheJobStopsWorking()
    {
        var replying = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"), Now);
        var working = new WorkingViewModel();

        working.Show(Board(replying));
        var streaming = working.IsShown;
        working.Show(Board(replying.Apply(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded), Now).Apply(new TurnCompleted(session, turn, TurnOutcome.Finished), Now), JobStatus.AwaitingReview));

        Assert.Equal((false, false), (streaming, working.IsShown));
    }

    private static Transcript Job() => Transcript.Empty;

    private static BoardJob Board(Transcript transcript, JobStatus status = JobStatus.Running) =>
        new(new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary, transcript);
}
