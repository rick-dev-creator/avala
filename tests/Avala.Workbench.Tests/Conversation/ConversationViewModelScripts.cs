using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ConversationViewModelScripts
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();

    [Fact]
    public void EntriesFollowTheTranscriptAndAStreamingMessageIsUpdatedInPlace()
    {
        var job = Job().WithPrompts("Fix the failing test", []).Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("think"), ItemKind.Reasoning, "Thinking"), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"), Now)
            .Apply(new ItemProgressed(session, turn, new ItemId("reply"), "Hello "), Now);
        var conversation = Open();
        conversation.Show(Board(job));
        var message = Assert.IsType<MessageViewModel>(conversation.Entries[^1]);

        conversation.Show(Board(job.Apply(new ItemProgressed(session, turn, new ItemId("reply"), "team."), Now)));

        Assert.Equal([typeof(PromptViewModel), typeof(ReasoningViewModel), typeof(MessageViewModel)], conversation.Entries.Select(entry => entry.GetType()));
        Assert.Same(message, conversation.Entries[^1]);
        Assert.Equal(("Hello team.", true), (message.Text, message.IsStreaming));
    }

    [Fact]
    public void APermissionCardAppearsInPlaceOnlyOnceThePolicyLeavesItToAPerson()
    {
        var asked = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("migrate"), ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PermissionRequested(session, turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"), Now);
        var conversation = Open();
        conversation.Show(Board(asked));
        var hidden = conversation.Entries.Count;

        conversation.Show(Board(asked.Apply(new PolicyDecision(session, turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, Now))));

        Assert.Equal(2, hidden);
        Assert.Equal([typeof(ToolViewModel), typeof(PermissionCardViewModel), typeof(MessageViewModel)], conversation.Entries.Select(entry => entry.GetType()));
        Assert.True(Assert.IsType<PermissionCardViewModel>(conversation.Entries[1]).AwaitsYou);
    }

    [Fact]
    public void TheComposerFollowsTheStatusOfTheJob()
    {
        var conversation = Open();

        conversation.Show(Board(Job(), JobStatus.NeedsHelp));

        Assert.Equal((JobStatus.NeedsHelp, true), (conversation.Composer.Status, conversation.Composer.AcceptsMessages));
    }

    [Fact]
    public void TheHeaderShowsTheJobsTitleStatusAndPlanProgress()
    {
        var planned = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new PlanUpdated(session, turn, [new PlanStep("Find the rounding", PlanStepStatus.Done), new PlanStep("Round to minor units", PlanStepStatus.InProgress)]), Now);

        ViewModelScript.Given(Open())
            .When(conversation => conversation.Show(Board(planned)))
            .ThenNotified(nameof(ConversationViewModel.Title), nameof(ConversationViewModel.Status), nameof(ConversationViewModel.Plan))
            .Then(conversation => Assert.Equal(("Fix JPY rounding in invoice totals", JobStatus.Running, "1 of 2"), (conversation.Title, conversation.Status, conversation.Plan)));
    }

    [Fact]
    public void ACanvasStreamsInPlaceUntilItCompletes()
    {
        var started = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new CanvasStarted(session, turn, new ItemId("diagram"), "Rounding before and after", "text/vnd.mermaid"), Now);
        var conversation = Open();
        conversation.Show(Board(started));
        var canvas = Assert.IsType<CanvasViewModel>(Assert.Single(conversation.Entries));
        var streaming = canvas.IsStreaming;

        conversation.Show(Board(started.Apply(
            new CanvasSnapshot(new CanvasId(turn, new ItemId("diagram")), session, "Rounding before and after", "text/vnd.mermaid", "flowchart LR", CanvasStatus.Completed, false))));

        Assert.Same(canvas, Assert.Single(conversation.Entries));
        Assert.Equal((true, false), (streaming, canvas.IsStreaming));
    }

    [Fact]
    public void ShowingAnUnchangedTranscriptLeavesTheEntriesUntouched()
    {
        var job = Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []));
        var conversation = Open();
        conversation.Show(job);
        var changes = 0;
        conversation.Entries.CollectionChanged += (_, _) => changes++;

        conversation.Show(job with { Summary = job.Summary with { Status = JobStatus.Checking } });

        Assert.Equal((0, JobStatus.Checking), (changes, conversation.Status));
    }

    [Fact]
    public void AJobOfAnEarlierRunShowsWhereWhatTheBoardSawBegins()
    {
        var conversation = Open();

        conversation.Show(Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []).WithRestart()));

        Assert.Equal([typeof(PromptViewModel), typeof(RestartViewModel)], conversation.Entries.Select(entry => entry.GetType()));
    }

    [Fact]
    public void TheConversationPlacesItsJobAndItsPillFollowsTheBoard()
    {
        var conversation = Open();
        var running = Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []));
        conversation.Show(running with { Summary = running.Summary with { Connection = new Agents.Contracts.Connections.ConnectionName("claude-work") } });
        var first = (conversation.Place, conversation.Pill.Kind, conversation.Pill.Text);

        conversation.Show(running with { Summary = running.Summary with { Status = JobStatus.NeedsHelp }, Hold = HoldReason.Stalled });

        Assert.Equal(("repo · claude-work", Components.Status.StatusKind.Working, "Running"), first);
        Assert.Equal((Components.Status.StatusKind.Held, "Held · stalled"), (conversation.Pill.Kind, conversation.Pill.Text));
    }

    private static Transcript Job() => Transcript.Empty;

    private static BoardJob Board(Transcript transcript, JobStatus status = JobStatus.Running) =>
        new(new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary, transcript);

    private static ConversationViewModel Open() =>
        new Conversations(new JobSteering(new FakeJobs(), new JobBoard(), new QueuedMessages(new FakeJobs())), new HumanReplies(new FakePermissionAnswers(), new FakeAgents()), FakeLinks.Opening).Open(JobId.New());
}
