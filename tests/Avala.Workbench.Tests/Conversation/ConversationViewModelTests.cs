using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Cards;
using Avala.Workbench.Conversation;
using Avala.Workbench.Replies;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ConversationViewModelTests
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

    private static Transcript Job() => Transcript.Empty;

    private static BoardJob Board(Transcript transcript, JobStatus status = JobStatus.Running) =>
        new(new FakeCatalog().Add("Fix the failing test", status).Summary, transcript);

    private static ConversationViewModel Open() =>
        new Conversations(new JobSteering(new FakeJobs(), new JobBoard()), new HumanReplies(new FakePermissionAnswers(), new FakeAgents())).Open(JobId.New());
}
