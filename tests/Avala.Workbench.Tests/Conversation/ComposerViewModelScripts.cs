using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class ComposerViewModelScripts : IAsyncDisposable
{
    private readonly FakeJobs jobs = new();
    private readonly SendKeys keys = new();
    private BoardJob shown = null!;
    private QueuedMessages queue = null!;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendingAMessageCarriesItsAttachmentsAndEmptiesTheTrayAsync()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        var tray = (AttachmentTrayViewModel)composer.Attachments;
        tray.Accept(new Avala.Agents.Contracts.Capabilities.AcceptsAttachments(["image/png"]));
        await tray.DropCommand.ExecuteAsync(["/home/ana/screen.png"]);
        composer.Draft = "Use this layout";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["screen.png"], jobs.Messages[^1].Attachments.Select(attached => attached.Name));
        Assert.Equal("Use this layout", jobs.Messages[^1].Text);
        Assert.Equal((false, string.Empty), (tray.HasAttachments, composer.Draft));
    }

    [Fact]
    public async Task ChoosingAMentionPutsItInTheDraftAndSendingAddsWhatItRefersToAsync()
    {
        var composer = Composer(JobStatus.NeedsHelp, Composing.Mentioning(new Worktree(), "src/Checkout/Rounding.cs"));
        var mentions = (MentionsViewModel)composer.Mentions;
        composer.Draft = "Look at @Rou";
        await mentions.Looking;

        mentions.ChooseCommand.Execute(null);
        var chosen = composer.Draft;
        composer.Draft += "first";
        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal("Look at @src/Checkout/Rounding.cs ", chosen);
        Assert.Equal("Look at @src/Checkout/Rounding.cs first\n\nReferenced:\n- @src/Checkout/Rounding.cs: the file src/Checkout/Rounding.cs in the job's worktree", jobs.Messages[^1].Text);
    }

    [Fact]
    public async Task ADraftComesBackWithItsAttachmentsAndMentionsWhenItsConversationIsOpenedAgainAsync()
    {
        var drafts = Composing.Drafts();
        var composer = Composer(JobStatus.NeedsHelp, drafts, Composing.Mentioning(new Worktree(), "src/Checkout/Rounding.cs"));
        var tray = (AttachmentTrayViewModel)composer.Attachments;
        tray.Accept(new Avala.Agents.Contracts.Capabilities.AcceptsAttachments(["image/png"]));
        await tray.DropCommand.ExecuteAsync(["/home/ana/screen.png"]);
        composer.Draft = "Look at @Rou";
        await ((MentionsViewModel)composer.Mentions).Looking;
        composer.Mentions.ChooseCommand.Execute(null);

        var reopened = Composer(JobStatus.NeedsHelp, drafts, Composing.Mentioning(new Worktree()), composer.Job);
        await reopened.SendCommand.ExecuteAsync(null);

        Assert.Equal("Look at @src/Checkout/Rounding.cs\n\nReferenced:\n- @src/Checkout/Rounding.cs: the file src/Checkout/Rounding.cs in the job's worktree", jobs.Messages[^1].Text);
        Assert.Equal(["screen.png"], jobs.Messages[^1].Attachments.Select(attached => attached.Name));
    }

    [Fact]
    public async Task SendingADraftForgetsItAsync()
    {
        var drafts = Composing.Drafts();
        var composer = Composer(JobStatus.NeedsHelp, drafts);
        composer.Draft = "Use the staging database";
        var kept = drafts.Of(composer.Job).IsSome;

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal((true, true), (kept, drafts.Of(composer.Job).IsNone));
    }

    [Fact]
    public void UpInAnEmptyComposerRecallsTheMessagesSentBeforeNewestFirstAndDownComesBack()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        Said(composer, "Use the ISO table");

        composer.RecallEarlierCommand.Execute(null);
        var newest = composer.Draft;
        composer.RecallEarlierCommand.Execute(null);
        var oldest = (composer.Draft, composer.RecallEarlierCommand.CanExecute(null));
        composer.RecallLaterCommand.Execute(null);
        var back = composer.Draft;
        composer.RecallLaterCommand.Execute(null);

        Assert.Equal("Use the ISO table", newest);
        Assert.Equal(("Fix JPY rounding in invoice totals", false), oldest);
        Assert.Equal("Use the ISO table", back);
        Assert.Equal((string.Empty, false), (composer.Draft, composer.RecallLaterCommand.CanExecute(null)));
    }

    [Fact]
    public void UpInAnEmptyComposerLeavesOutAMessageAvalaWroteToContinueTheJob()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        Said(composer, "The usage window has reset.", Avala.Agents.Contracts.Sessions.MessageAuthor.Avala);

        composer.RecallEarlierCommand.Execute(null);

        Assert.Equal(("Fix JPY rounding in invoice totals", false), (composer.Draft, composer.RecallEarlierCommand.CanExecute(null)));
    }

    [Fact]
    public void UpInAnEmptyComposerLeavesOutANoteAvalaSentWhileTheAgentWorked()
    {
        var composer = Composer(JobStatus.Running);
        var session = Avala.Agents.Contracts.Sessions.SessionId.New();
        var turn = Avala.Agents.Contracts.Sessions.TurnId.New();
        shown = shown with
        {
            Transcript = shown.Transcript.WithPrompts(shown.Summary.Instruction, [new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Running, Option<string>.None, Option<Avala.Agents.Contracts.Sessions.SessionId>.None)])
                .Apply(new Avala.Agents.Contracts.Events.MessageQueued(session, turn, "Avala now allows running `dotnet test` in this job. You may retry it.") { Author = Avala.Agents.Contracts.Sessions.MessageAuthor.Avala }, DateTimeOffset.UnixEpoch),
        };
        composer.Track(shown);

        composer.RecallEarlierCommand.Execute(null);

        Assert.Equal(("Fix JPY rounding in invoice totals", false), (composer.Draft, composer.RecallEarlierCommand.CanExecute(null)));
    }

    [Fact]
    public void WithTextInTheComposerUpRecallsNothing()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        Said(composer, "Use the ISO table");

        composer.Draft = "Also cover";

        Assert.False(composer.RecallEarlierCommand.CanExecute(null));
    }

    [Fact]
    public async Task ASentMessageContinuesTheJobAndClearsTheDraft()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        composer.Draft = " Use the staging database ";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["continue Use the staging database"], jobs.Calls);
        Assert.Equal((string.Empty, string.Empty), (composer.Draft, composer.Error));
    }

    [Fact]
    public async Task AMessageToAJobAwaitingReviewSendsItBack()
    {
        var composer = Composer(JobStatus.AwaitingReview);
        composer.Draft = "Also limit by account";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["send back Also limit by account"], jobs.Calls);
    }

    [Fact]
    public async Task ARefusedMessageStaysInTheDraftWithTheReason()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        jobs.Refusal = JobRejection.WorkspaceUnavailable;
        composer.Draft = "Use the staging database";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(("Use the staging database", "The job's worktree is gone."), (composer.Draft, composer.Error));
    }

    [Fact]
    public async Task AMessageToARunningAgentThatTakesMessagesMidTurnJoinsTheRunningTurn()
    {
        var composer = Composer(JobStatus.Running, takesMessagesMidTurn: true);
        composer.Draft = "Keep the old namespace as an alias";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal(["steer Keep the old namespace as an alias"], jobs.Calls);
        Assert.Equal((string.Empty, string.Empty, string.Empty), (composer.Draft, composer.Queued, composer.Error));
        Assert.Equal(("Message the agent while it works…", true, true), (composer.Placeholder, composer.InterruptCommand.CanExecute(null), composer.StopCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AMessageToARunningAgentThatTakesNoMessageMidTurnIsQueuedHonestly()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the old namespace as an alias";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Empty(jobs.Calls);
        Assert.Equal((string.Empty, "Keep the old namespace as an alias", true), (composer.Draft, composer.Queued, composer.WithdrawCommand.CanExecute(null)));
        Assert.Equal(("This agent takes no message mid-turn · queue one for when it stops…", "Queue for when the agent stops"), (composer.Placeholder, composer.SendHint));
    }

    [Fact]
    public async Task AnAgentThatRefusesTheMidTurnMessageGetsItQueuedInstead()
    {
        var composer = Composer(JobStatus.Running, takesMessagesMidTurn: true);
        jobs.Refusal = JobRejection.NotSteerable;
        composer.Draft = "Keep the alias";

        await composer.SendCommand.ExecuteAsync(null);

        Assert.Equal((string.Empty, "Keep the alias", string.Empty), (composer.Draft, composer.Queued, composer.Error));
    }

    [Fact]
    public async Task QueuedMessagesContinueTheJobTogetherWhenItNextNeedsYou()
    {
        var composer = Composer(JobStatus.Checking);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);
        composer.Draft = "And update the changelog";
        await composer.SendCommand.ExecuteAsync(null);

        jobs.Status = JobStatus.NeedsHelp;
        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.NeedsHelp), Cancellation);
        Becomes(composer, JobStatus.NeedsHelp);

        Assert.Equal(["continue Keep the alias\n\nAnd update the changelog"], jobs.Calls);
        Assert.Equal((string.Empty, false), (composer.Queued, composer.WithdrawCommand.CanExecute(null)));
    }

    [Fact]
    public async Task AQueuedMessageWaitsInTheReviewUntilAPersonChoosesToSendItBack()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);

        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.AwaitingReview), Cancellation);
        Becomes(composer, JobStatus.AwaitingReview);

        Assert.Empty(jobs.Calls);
        Assert.Equal(("Keep the alias", "Queued · waits in the review: send back with it, or withdraw it"), (composer.Queued, composer.QueuedCaption));
    }

    [Fact]
    public async Task AWithdrawnMessageIsNeverDelivered()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);

        composer.WithdrawCommand.Execute(null);
        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.NeedsHelp), Cancellation);

        Assert.Equal(string.Empty, composer.Queued);
        Assert.Empty(jobs.Calls);
    }

    [Fact]
    public async Task AQueuedMessageForAJobThatEndsIsDropped()
    {
        var composer = Composer(JobStatus.Running);
        composer.Draft = "Keep the alias";
        await composer.SendCommand.ExecuteAsync(null);

        await queue.HandleAsync(new JobProgressed(shown.Job, JobStatus.Failed), Cancellation);
        Becomes(composer, JobStatus.Failed);

        Assert.Equal((string.Empty, false), (composer.Queued, composer.SendCommand.CanExecute(null)));
        Assert.Empty(jobs.Calls);
    }

    [Fact]
    public async Task InterruptingAndStoppingHoldTheJobForTheirReasons()
    {
        var composer = Composer(JobStatus.Running);

        await composer.InterruptCommand.ExecuteAsync(null);
        await composer.StopCommand.ExecuteAsync(null);

        Assert.Equal(["hold Interrupted", "hold Stopped"], jobs.Calls);
    }

    [Fact]
    public async Task AStopTheJobRefusesIsReported()
    {
        var composer = Composer(JobStatus.Running);
        jobs.Refusal = JobRejection.NotRunning;

        await composer.StopCommand.ExecuteAsync(null);

        Assert.Equal("The job is not running.", composer.Error);
    }

    [Fact]
    public void AnEmptyDraftCannotBeSent()
    {
        var composer = Composer(JobStatus.NeedsHelp);
        composer.Draft = "  ";

        Assert.False(composer.SendCommand.CanExecute(null));
    }

    [Fact]
    public void AJobThatStopsRunningTakesMessagesInsteadOfInterruptions() =>
        ViewModelScript.Given(Composer(JobStatus.Running))
            .When(composer =>
            {
                composer.Draft = "Carry on";
                Becomes(composer, JobStatus.NeedsHelp);
            })
            .ThenNotified(nameof(ComposerViewModel.Status), nameof(ComposerViewModel.AcceptsMessages), nameof(ComposerViewModel.Placeholder), nameof(ComposerViewModel.SendHint))
            .Then(composer => Assert.Equal((true, false, false), (composer.SendCommand.CanExecute(null), composer.InterruptCommand.CanExecute(null), composer.StopCommand.CanExecute(null))))
            .Then(composer => Assert.Equal("Continue the job with a message…", composer.Placeholder));

    [Fact]
    public void AJobHeldAsStalledSaysWhyInItsComposerAndStillTakesAMessage() =>
        ViewModelScript.Given(Composer(JobStatus.Running))
            .When(composer =>
            {
                shown = shown with { Summary = shown.Summary with { Status = JobStatus.NeedsHelp }, Hold = HoldReason.Stalled };
                composer.Draft = "Carry on";
                composer.Track(shown);
            })
            .Then(composer => Assert.Equal(("Held · stalled · send a message to continue the job…", true), (composer.Placeholder, composer.SendCommand.CanExecute(null))));

    [Fact]
    public async Task ASecondSendWhileTheFirstIsInFlightIsRefusedByTheCommand()
    {
        var gate = new TaskCompletionSource();
        var composer = Composer(JobStatus.NeedsHelp, new GatedJobs(gate.Task, jobs));
        composer.Draft = "Carry on";

        var first = composer.SendCommand.ExecuteAsync(null);
        var again = composer.SendCommand.CanExecute(null);
        gate.SetResult();
        await first;

        Assert.False(again);
        Assert.Single(jobs.Calls);
    }

    private ComposerViewModel Composer(JobStatus status, bool takesMessagesMidTurn = false) => Composer(status, jobs, takesMessagesMidTurn);

    private ComposerViewModel Composer(JobStatus status, MentionsViewModel mentions) => Composer(status, jobs, false, mentions);

    private ComposerViewModel Composer(JobStatus status, Avala.Workbench.Drafting.DraftBook drafts, MentionsViewModel? mentions = null, JobId? job = null)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary;
        summary = job is { } same ? summary with { Job = same } : summary;
        shown = new BoardJob(summary, Transcript.Empty);
        jobs.Status = status;
        queue = new QueuedMessages(jobs);
        var tools = Composing.Tools(keys, summary.Job) with { Drafts = drafts };
        var composer = new ComposerViewModel(summary.Job, new JobSteering(jobs, queue), mentions is null ? tools : tools with { Mentions = mentions }, Composing.Harness(summary.Job));
        composer.Track(shown);

        return composer;
    }

    private ComposerViewModel Composer(JobStatus status, IJobs steered, bool takesMessagesMidTurn = false, MentionsViewModel? mentions = null)
    {
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary;
        shown = new BoardJob(summary, Transcript.Empty) { TakesMessagesMidTurn = takesMessagesMidTurn };
        jobs.Status = status;
        jobs.Steerable = takesMessagesMidTurn;
        queue = new QueuedMessages((IJobMessages)steered);
        var composer = new ComposerViewModel(summary.Job, new JobSteering(steered, queue), mentions is null ? Composing.Tools(keys, summary.Job) : Composing.Tools(keys, summary.Job) with { Mentions = mentions }, Composing.Harness(summary.Job));
        composer.Track(shown);

        return composer;
    }

    private void Said(ComposerViewModel composer, string hint, Avala.Agents.Contracts.Sessions.MessageAuthor author = Avala.Agents.Contracts.Sessions.MessageAuthor.Person)
    {
        shown = shown with
        {
            Transcript = Transcript.Empty.WithPrompts(
                shown.Summary.Instruction,
                [
                    new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Passed, Option<string>.None, Option<Avala.Agents.Contracts.Sessions.SessionId>.None),
                    new AttemptRecord(2, AttemptOrigin.Hint, AttemptOutcome.Passed, hint, Option<Avala.Agents.Contracts.Sessions.SessionId>.None) { Author = author },
                ]),
        };
        composer.Track(shown);
    }

    private void Becomes(ComposerViewModel composer, JobStatus status)
    {
        shown = shown with { Summary = shown.Summary with { Status = status } };
        jobs.Status = status;
        composer.Track(shown);
    }

    public ValueTask DisposeAsync() => queue is { } owned ? owned.DisposeAsync() : ValueTask.CompletedTask;

    private sealed class GatedJobs(Task gate, FakeJobs inner) : IJobs, IJobMessages
    {
        public ValueTask<Avala.Sdk.Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) => inner.SubmitAsync(request, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => inner.HoldRunningAsync(job, reason, cancellationToken);


        public ValueTask<Avala.Sdk.Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => inner.HoldAsync(job, reason, cancellationToken);

        public async ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken)
        {
            await gate;

            return await inner.ContinueAsync(job, message, cancellationToken);
        }

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ContinueOnAsync(
            JobId job,
            Avala.Agents.Contracts.Connections.ConnectionName connection,
            string message,
            CancellationToken cancellationToken) =>
            inner.ContinueOnAsync(job, connection, message, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) => inner.DiscardAsync(job, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => inner.ApproveAsync(job, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => inner.SendBackAsync(job, feedback, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => inner.ResumeAsync(job, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) => inner.HandOffAsync(job, handoff, cancellationToken);

        public async ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken)
        {
            await gate;

            return await inner.ContinueAsync(job, message, cancellationToken);
        }

        public ValueTask<Avala.Sdk.Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken) => inner.SteerAsync(job, message, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, JobMessage message, CancellationToken cancellationToken) => inner.SendBackAsync(job, message, cancellationToken);

        public ValueTask<Avala.Sdk.Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, Avala.Agents.Contracts.Sessions.ModelChoice model, CancellationToken cancellationToken) => inner.ChooseModelAsync(job, model, cancellationToken);
    }
}
