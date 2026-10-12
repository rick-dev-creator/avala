using Avala.Sdk.Appearance;
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
    public void TheHeaderShowsTheJobsTitleStatusAndThePanelItsPlanProgress()
    {
        var planned = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new PlanUpdated(session, turn, [new PlanStep("Find the rounding", PlanStepStatus.Done), new PlanStep("Round to minor units", PlanStepStatus.InProgress)]), Now);

        ViewModelScript.Given(Open())
            .When(conversation => conversation.Show(Board(planned)))
            .ThenNotified(nameof(ConversationViewModel.Title), nameof(ConversationViewModel.Status))
            .Then(conversation => Assert.Equal(
                ("Fix JPY rounding in invoice totals", JobStatus.Running, "1 of 2", true),
                (conversation.Title, conversation.Status, conversation.Plan.Progress, conversation.Plan.IsShown)));
    }

    [Fact]
    public void ThePlanStaysAboveTheComposerAcrossTurnsUntilEveryStepIsDone()
    {
        var later = TurnId.New();
        var last = TurnId.New();
        var planned = Job().Apply(new TurnStarted(session, turn), Now)
            .Apply(new PlanUpdated(session, turn, [new PlanStep("Write the change", PlanStepStatus.InProgress), new PlanStep("Run the tests", PlanStepStatus.Pending)]), Now)
            .Apply(new TurnCompleted(session, turn, TurnOutcome.Finished), Now);
        var quiet = planned.Apply(new TurnStarted(session, later), Now).Apply(new TurnCompleted(session, later, TurnOutcome.Finished), Now);
        var done = quiet.Apply(new TurnStarted(session, last), Now)
            .Apply(new PlanUpdated(session, last, [new PlanStep("Write the change", PlanStepStatus.Done), new PlanStep("Run the tests", PlanStepStatus.Done)]), Now);

        ViewModelScript.Given(Open())
            .When(conversation => conversation.Show(Board(planned)))
            .When(conversation => conversation.Show(Board(quiet)))
            .Then(conversation => Assert.Equal((true, "0 of 2", "Write the change"), (conversation.Plan.IsShown, conversation.Plan.Progress, conversation.Plan.Current)))
            .When(conversation => conversation.Show(Board(done)))
            .Then(conversation => Assert.Equal((false, "2 of 2"), (conversation.Plan.IsShown, conversation.Plan.Progress)));
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

        conversation.Show(Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []).WithRestart(kept: false)));

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

    [Fact]
    public void TheHeaderAddsTheModelAndEffortTheSessionReportsItRanWithOnlyOnceReported()
    {
        var conversation = Open();
        var running = Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []).Apply(new TurnStarted(session, turn), Now));
        conversation.Show(running);
        var before = conversation.Place;

        conversation.Show(running with { Transcript = running.Transcript.Apply(new ModelReported(session, turn, "claude-opus-5-5") { Effort = "high" }, Now) });

        Assert.Equal(("repo", "repo · claude-opus-5-5\u00A0· high\u00A0effort"), (before, conversation.Place));
    }

    [Fact]
    public void EditingYourPromptPutsItsTextInTheComposer()
    {
        var conversation = Open();
        conversation.Show(Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []), JobStatus.NeedsHelp));
        var prompt = Assert.IsType<PromptViewModel>(Assert.Single(conversation.Entries));

        prompt.EditCommand.Execute(null);

        Assert.Equal("Fix JPY rounding in invoice totals", conversation.Composer.Draft);
    }

    [Fact]
    public async Task RetryingYourPromptSendsItAgainAsANewMessage()
    {
        var jobs = new FakeJobs { Status = JobStatus.NeedsHelp };
        var conversation = Open(jobs);
        conversation.Show(Board(Job().WithPrompts("Fix JPY rounding in invoice totals", []), JobStatus.NeedsHelp));
        var prompt = Assert.IsType<PromptViewModel>(Assert.Single(conversation.Entries));

        prompt.RetryCommand.Execute(null);
        await (((ComposerViewModel)conversation.Composer).ResendCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(["continue Fix JPY rounding in invoice totals"], jobs.Calls);
        Assert.Equal(string.Empty, conversation.Composer.Draft);
    }

    [Theory]
    [InlineData(AttemptOrigin.Hint, JobStatus.NeedsHelp, true, true)]
    [InlineData(AttemptOrigin.Hint, JobStatus.Running, true, false)]
    [InlineData(AttemptOrigin.Hint, JobStatus.Approved, false, false)]
    [InlineData(AttemptOrigin.Retry, JobStatus.NeedsHelp, false, false)]
    public void OnlyYourPromptsCanBeEditedAndOnlyWhileTheJobTakesAMessageCanTheyBeSentAgain(AttemptOrigin origin, JobStatus status, bool edits, bool resends)
    {
        var conversation = Open();
        var attempts = new[] { new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Passed, "Fix it", Option<SessionId>.None), new AttemptRecord(2, origin, AttemptOutcome.Running, "Keep the alias", Option<SessionId>.None) };

        conversation.Show(Board(Job().WithPrompts("Fix it", attempts), status));

        var prompt = Assert.IsType<PromptViewModel>(conversation.Entries[^1]);
        Assert.Equal((edits, resends), (prompt.EditCommand.CanExecute(null), prompt.RetryCommand.CanExecute(null)));
    }

    [Fact]
    public async Task CopyAsMarkdownPutsTheConversationOnTheClipboardWithTheOutputOfTheToolsYouExpandedAsync()
    {
        var clipboard = new FakeClipboard();
        var conversation = Open(new FakeJobs(), clipboard, new FakeFileSaver());
        conversation.Show(Board(Ran()));
        await conversation.Entries.OfType<ToolViewModel>().Last().ToggleCommand.ExecuteAsync(null);

        await conversation.CopyMarkdownCommand.ExecuteAsync(null);

        Assert.Equal("# Fix JPY rounding in invoice totals\n\n**You**\n\nFix the failing test\n\n- Run go test ./...\n\n```\nok  money\n```\n\nAll green.\n", Assert.Single(clipboard.Copied));
    }

    [Fact]
    public async Task ExportSavesTheConversationAsAMarkdownFileNamedAfterTheJobAsync()
    {
        var saver = new FakeFileSaver();
        var conversation = Open(new FakeJobs(), new FakeClipboard(), saver);
        conversation.Show(Board(Ran()));

        await conversation.ExportCommand.ExecuteAsync(null);

        var saved = Assert.Single(saver.Saved);
        Assert.Equal(("fix-jpy-rounding-in-invoice-totals.md", "# Fix JPY rounding in invoice totals\n\n**You**\n\nFix the failing test\n\n- Run go test ./...\n\nAll green.\n"), saved);
        Assert.Equal(string.Empty, conversation.Notice);
    }

    [Theory]
    [InlineData(true, "# Ask r•••••@g••••.com\n\n**You**\n\nRead ~/notes.md for r•••••@g••••.com\n\n- Read ~/notes.md\n")]
    [InlineData(false, "# Ask rick@gmail.com\n\n**You**\n\nRead /home/rick/notes.md for rick@gmail.com\n\n- Read /home/rick/notes.md\n")]
    public async Task AnExportMasksEmailsAndTheHomeFolderOnlyWhilePersonalDetailsAreHiddenAsync(bool hidden, string markdown)
    {
        var saver = new FakeFileSaver();
        var conversation = Open(new FakeJobs(), new FakeClipboard(), saver, new FakeAppearance(AppearancePreference.Default with { HidesPersonalDetails = hidden }));
        conversation.Show(new(
            new FakeCatalog().Add("Ask rick@gmail.com", JobStatus.Running).Summary,
            Job().WithPrompts("Read /home/rick/notes.md for rick@gmail.com", []).Apply(new TurnStarted(session, turn), Now)
                .Apply(new ItemStarted(session, turn, new ItemId("read"), ItemKind.FileEdit, "Read /home/rick/notes.md"), Now)
                .Apply(new ItemCompleted(session, turn, new ItemId("read"), ItemOutcome.Succeeded), Now)));

        await conversation.ExportCommand.ExecuteAsync(null);

        Assert.Equal(markdown, Assert.Single(saver.Saved).Item2);
    }

    [Theory]
    [InlineData(FileSaveError.Cancelled, "")]
    [InlineData(FileSaveError.Unwritable, "The conversation could not be saved there.")]
    public async Task AnExportThatDidNotSaveSaysWhyUnlessYouCancelledItAsync(FileSaveError refusal, string notice)
    {
        var conversation = Open(new FakeJobs(), new FakeClipboard(), new FakeFileSaver { Refusal = refusal });
        conversation.Show(Board(Ran()));

        await conversation.ExportCommand.ExecuteAsync(null);

        Assert.Equal(notice, conversation.Notice);
    }

    private Transcript Ran() =>
        Job().WithPrompts("Fix the failing test", []).Apply(new TurnStarted(session, turn), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("test"), ItemKind.Command, "Run go test ./...") { Input = "go test ./..." }, Now)
            .Apply(new ItemProgressed(session, turn, new ItemId("test"), "ok  money\n"), Now)
            .Apply(new ItemCompleted(session, turn, new ItemId("test"), ItemOutcome.Succeeded), Now)
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"), Now)
            .Apply(new ItemProgressed(session, turn, new ItemId("reply"), "All green."), Now)
            .Apply(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded), Now);

    private static Transcript Job() => Transcript.Empty;

    private static BoardJob Board(Transcript transcript, JobStatus status = JobStatus.Running) =>
        new(new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary, transcript);

    private static ConversationViewModel Open() => Open(new FakeJobs());

    private static ConversationViewModel Open(FakeJobs jobs) => Open(jobs, new FakeClipboard(), new FakeFileSaver());

    private static ConversationViewModel Open(FakeJobs jobs, FakeClipboard clipboard, FakeFileSaver saver, FakeAppearance? appearance = null) =>
        new Conversations(new JobSteering(jobs, new QueuedMessages(jobs)), new TimelineItems(new HumanReplies(new FakePermissionAnswers(), new FakeAgents()), FakeLinks.Opening, clipboard, Tools.Files()), new SendKeys(), new TranscriptExport(clipboard, saver, appearance ?? new FakeAppearance())).Open(JobId.New());
}
