using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using static Avala.Documentation.Tests.Writing.Documented;

namespace Avala.Documentation.Tests.Writing;

public sealed class DocumentationDeskTests
{
    private const string Folder = "docs/features/handle-stripe-refund-webhooks";

    [Fact]
    public async Task AJobsPlanStartsADocumentationCompanionOnTheResearchRoleBriefedWithThePlanAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();

        await documented.PlanAsync(session, "Add a Webhooks container", "Write the refund to the ledger");

        var (writer, request) = Assert.Single(documented.Jobs.Submitted);
        Assert.Equal((Option<JobId>.Some(job), true, Option<Autonomy>.Some(Autonomy.Autonomous), FixedRoles.Research), (request.Parent, request.Companion, request.Autonomy, request.Model));
        Assert.Contains("1. Add a Webhooks container\n2. Write the refund to the ledger\n", request.Instruction);
        Assert.Equal([new VersionWriting(job, writer, 1, VersionKind.Proposed)], documented.Published<VersionWriting>());
        Assert.Equal([new FeatureChosen(job, FeatureChoice.Of(new Feature("handle-stripe-refund-webhooks", "Handle Stripe refund webhooks")))], documented.Published<FeatureChosen>());
    }

    [Fact]
    public async Task AWriterReadyForReviewHasItsDraftCopiedIntoTheJobsWorktreeWithItsPlanAndIndexAndIsDiscardedAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var writer = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(writer), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\nThe C4.\n");

        await documented.ProgressAsync(writer, JobStatus.AwaitingReview);

        var worktree = FakeJobs.Worktree(job);
        var written = Assert.Single(documented.Published<VersionWritten>());
        Assert.Equal((job, 1, VersionKind.Proposed, $"{Folder}/v1-proposed.md", Option<JobId>.Some(writer)), (written.Job, written.Version.Number, written.Version.Kind, written.Version.Path, written.Version.Writer));
        Assert.Equal("# Handle Stripe refund webhooks\n\nThe C4.\n", documented.Files[worktree, $"{Folder}/v1-proposed.md"]);
        Assert.Equal("# Plan v1\n\n1. Add a Webhooks container\n", documented.Files[worktree, $"{Folder}/plans/v1.md"]);
        Assert.Contains("| v1 | Proposed | [v1-proposed.md](v1-proposed.md) |", documented.Files[worktree, $"{Folder}/README.md"]);
        Assert.Contains("href: v1-proposed.md", documented.Files[worktree, $"{Folder}/toc.yml"]);
        Assert.Equal([writer], documented.Jobs.Discarded);
    }

    [Fact]
    public async Task APlanStillBeingWrittenStartsNoWriterUntilAStepStartsOrTheTurnEndsAsync()
    {
        var started = Documented.With();
        var (_, first) = await started.RunningAsync();
        var ended = Documented.With();
        var (_, second) = await ended.RunningAsync();

        await started.PlanningAsync(first, "Create greet.py");
        await started.PlanningAsync(first, "Create greet.py", "Create README.md");
        var whilePlanning = started.Jobs.Submitted.Count;
        await started.PlanAsync(first, "Create greet.py", "Create README.md");
        await ended.PlanningAsync(second, "Create greet.py", "Create README.md");
        await ended.TurnEndsAsync(second);

        Assert.Equal(0, whilePlanning);
        Assert.All(new[] { started, ended }, documented => Assert.Contains("1. Create greet.py\n2. Create README.md\n", Assert.Single(documented.Jobs.Submitted).Request.Instruction));
    }

    [Fact]
    public async Task AVersionWrittenWhileTheJobRunsWaitsForItsAttemptsCheckpointAndOneWrittenAtReviewIsCheckpointedAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var writer = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(writer), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n");
        await documented.ProgressAsync(writer, JobStatus.AwaitingReview);
        var whileRunning = documented.Jobs.Checkpoints.Count;

        await documented.ProgressAsync(job, JobStatus.AwaitingReview);

        Assert.Equal(0, whileRunning);
        Assert.Equal([(new WorkspaceId(job.Value), "Document Handle Stripe refund webhooks, version 2")], documented.Jobs.Checkpoints);
    }

    [Fact]
    public async Task ALaterJobOnAnExistingFeatureIsSuggestedItAndContinuesItsNumberingAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync("Retry refund webhooks that Stripe sends twice");
        var worktree = FakeJobs.Worktree(job);
        documented.Files.Draft(worktree, "docs/features/refund-webhooks/README.md", "# Refund webhooks\n");
        documented.Files.Draft(worktree, "docs/features/refund-webhooks/v1-proposed.md", "# Refund webhooks\n");
        documented.Files.Draft(worktree, "docs/features/refund-webhooks/v3-as-built.md", "# Refund webhooks\n\nAs built.\n");

        await documented.PlanAsync(session, "Skip a refund whose key exists");

        Assert.Equal([new VersionWriting(job, documented.LastWriter, 4, VersionKind.Proposed)], documented.Published<VersionWriting>());
        Assert.Equal(Option<Feature>.Some(new Feature("refund-webhooks", "Refund webhooks")), Outcomes.Present(documented.Book.Of(job)).Choice.Feature);
        Assert.Contains("The previous version, to build on:\n\n# Refund webhooks\n\nAs built.", documented.Jobs.Submitted[^1].Request.Instruction);
    }

    [Fact]
    public async Task TheSamePlanAgainStartsNoWriterAndAChangedPlanAfterAVersionStartsADeviationNumberedAfterItAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        await documented.PlanAsync(session, "Add a Webhooks container");
        var first = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(first), DocumentationDraft.Path, "# v1\n");
        await documented.ProgressAsync(first, JobStatus.AwaitingReview);
        await documented.PlanAsync(session, "Add a Webhooks container");

        await documented.PlanAsync(session, "Add a Webhooks container", "Keep idempotency keys in the ledger");

        Assert.Equal(2, documented.Jobs.Submitted.Count);
        Assert.Equal(new VersionWriting(job, documented.LastWriter, 2, VersionKind.Deviation), documented.Published<VersionWriting>()[^1]);
        Assert.Contains("The previous version, to build on:\n\n# v1\n", documented.Jobs.Submitted[^1].Request.Instruction);
    }

    [Fact]
    public async Task APlanChangedWhileItsVersionIsWrittenIsDocumentedNextBeforeAsBuiltEvenOnceTheJobAwaitsReviewAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var first = documented.LastWriter;
        await documented.PlanAsync(session, "Add a Webhooks container", "Keep idempotency keys in the ledger");
        await documented.ProgressAsync(job, JobStatus.AwaitingReview);
        documented.Files.Draft(FakeJobs.Worktree(first), DocumentationDraft.Path, "# v1\n");
        await documented.ProgressAsync(first, JobStatus.AwaitingReview);
        var second = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(second), DocumentationDraft.Path, "# v2\n");

        await documented.ProgressAsync(second, JobStatus.AwaitingReview);

        Assert.Equal([(1, VersionKind.Proposed), (2, VersionKind.Deviation), (3, VersionKind.AsBuilt)], documented.Published<VersionWritten>().Select(written => (written.Version.Number, written.Version.Kind)));
        Assert.Contains("1. Add a Webhooks container\n2. Keep idempotency keys in the ledger\n", documented.Jobs.Submitted[^1].Request.Instruction);
    }

    [Fact]
    public async Task APlanChangedWhileItsVersionIsWrittenIsStillDocumentedNextAfterARestartAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var first = documented.LastWriter;
        await documented.PlanAsync(session, "Add a Webhooks container", "Keep idempotency keys in the ledger");
        await documented.ProgressAsync(job, JobStatus.AwaitingReview);
        documented.Files.Draft(FakeJobs.Worktree(first), DocumentationDraft.Path, "# v1\n");
        documented.Jobs.Move(first, JobStatus.AwaitingReview);

        _ = await documented.RestartedAsync();

        Assert.Equal([(1, VersionKind.Proposed)], documented.Published<VersionWritten>().Select(written => (written.Version.Number, written.Version.Kind)));
        Assert.Equal(new VersionWriting(job, documented.LastWriter, 2, VersionKind.Deviation), documented.Published<VersionWriting>()[^1]);
        Assert.Contains("1. Add a Webhooks container\n2. Keep idempotency keys in the ledger\n", documented.Jobs.Submitted[^1].Request.Instruction);
    }

    [Fact]
    public async Task AStepTitleOnTwoLinesReachesTheWriterAsOneStepAsync()
    {
        var documented = Documented.With();
        var (_, session) = await documented.RunningAsync();

        await documented.PlanAsync(session, "Write the changelog\nwith a date per entry", "Link it");

        Assert.Contains("2. Link it\n", documented.Jobs.Submitted[^1].Request.Instruction);
    }

    [Theory]
    [InlineData(JobStatus.AwaitingReview, "NoDraft", true)]
    [InlineData(JobStatus.NeedsHelp, "WriterStopped", true)]
    [InlineData(JobStatus.Failed, "WriterStopped", false)]
    public async Task AWriterThatLeavesNoDraftOrStopsWritesNoVersionSaysWhyAndIsNotRetriedForTheSamePlanAsync(JobStatus status, string failure, bool discarded)
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var writer = documented.LastWriter;

        await documented.ProgressAsync(writer, status);

        Assert.Equal([new VersionNotWritten(job, VersionKind.Proposed, Enum.Parse<VersionFailure>(failure))], documented.Published<VersionNotWritten>());
        Assert.Empty(documented.Published<VersionWritten>());
        Assert.Equal(discarded, documented.Jobs.Discarded.Contains(writer));
        Assert.Equal(Option<JobId>.None, Outcomes.Present(documented.Book.Of(job)).Writer.Map(pending => pending.Writer));
        Assert.Single(documented.Jobs.Submitted);
    }

    [Fact]
    public async Task AWriterHeldAtItsUsageLimitStillWritesTheVersionOfItsPlanOnceTheLimitResetsAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var held = documented.LastWriter;
        documented.Jobs.Held(held, HoldReason.LimitNearlyReached);
        await documented.Desk.HandleAsync(new JobProgressed(held, JobStatus.NeedsHelp), Cancellation);

        await documented.TurnEndsAsync(session);
        var writer = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(writer), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\nThe C4.\n");
        await documented.ProgressAsync(writer, JobStatus.AwaitingReview);

        Assert.Equal([(job, VersionKind.Proposed)], documented.Published<VersionWritten>().Select(written => (written.Job, written.Version.Kind)));
    }

    [Fact]
    public async Task AWriterHeldAtItsUsageLimitLeavesItsVersionWaitingUntilItResumesAndNothingUnwrittenOnceItWritesAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var writer = documented.LastWriter;
        documented.Jobs.Held(writer, HoldReason.LimitNearlyReached);

        await documented.Desk.HandleAsync(new JobProgressed(writer, JobStatus.NeedsHelp), Cancellation);
        var waiting = Outcomes.Present(documented.Book.Of(job)).Waiting.Map(version => (version.Kind, version.Hold));
        await documented.ProgressAsync(writer, JobStatus.Running);
        var resumed = Outcomes.Present(documented.Book.Of(job)).Waiting;
        documented.Files.Draft(FakeJobs.Worktree(writer), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\nThe C4.\n");
        await documented.ProgressAsync(writer, JobStatus.AwaitingReview);

        Assert.Equal(Option<(VersionKind, Option<HoldReason>)>.Some((VersionKind.Proposed, HoldReason.LimitNearlyReached)), waiting);
        Assert.Equal(Option<UnwrittenVersion>.None, resumed);
        Assert.Single(documented.Published<VersionWritten>());
        Assert.Empty(Outcomes.Present(documented.Book.Of(job)).Unwritten);
        Assert.Empty(documented.Published<VersionNotWritten>());
    }

    [Fact]
    public async Task AWaitingWriterWhoseJobEndsIsStoppedAndItsVersionIsShownNotWrittenAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        await documented.PlanAsync(session, "Add a Webhooks container");
        var writer = documented.LastWriter;
        documented.Jobs.Held(writer, HoldReason.LimitNearlyReached);
        await documented.Desk.HandleAsync(new JobProgressed(writer, JobStatus.NeedsHelp), Cancellation);

        await documented.Desk.HandleAsync(new JobProgressed(job, JobStatus.Discarded), Cancellation);

        Assert.Equal([new VersionNotWritten(job, VersionKind.Proposed, VersionFailure.WriterStopped) { Hold = HoldReason.LimitNearlyReached }], documented.Published<VersionNotWritten>());
        Assert.Contains(writer, documented.Jobs.Discarded);
        Assert.Equal(Option<UnwrittenVersion>.None, Outcomes.Present(documented.Book.Of(job)).Waiting);
    }

    [Fact]
    public async Task AWriterTheJobsRefuseLeavesOneUnwrittenLineForItsPlanNotOneEachTimeThePlanMovesOrATurnEndsAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        documented.Jobs.Rejection = JobRejection.InvalidRequest;

        await documented.PlanAsync(session, "Add a Webhooks container", "Write the refund to the ledger");
        await documented.PlanAsync(session, "Add a Webhooks container", "Write the refund to the ledger");
        await documented.TurnEndsAsync(session);

        Assert.Single(documented.Published<VersionNotWritten>());
        Assert.Single(Outcomes.Present(documented.Book.Of(job)).Unwritten);
    }

    [Fact]
    public async Task AtStartupAWriterStillPreparingIsLeftToFinishAndOneTheCatalogNoLongerKnowsIsReportedStoppedAsync()
    {
        var documented = Documented.With();
        var (preparing, first) = await documented.RunningAsync("Handle refunds");
        var (forgotten, second) = await documented.RunningAsync("Schedule payouts");
        await documented.PlanAsync(first, "Add a Webhooks container");
        var stillPreparing = documented.LastWriter;
        await documented.PlanAsync(second, "Add a scheduler");
        documented.Jobs.Forget(documented.LastWriter);

        await documented.Desk.HandleAsync(new StartupCompleted(), Cancellation);

        Assert.Equal([new VersionNotWritten(forgotten, VersionKind.Proposed, VersionFailure.WriterStopped)], documented.Published<VersionNotWritten>());
        Assert.Equal(Option<JobId>.Some(stillPreparing), Outcomes.Present(documented.Book.Of(preparing)).Writer.Map(pending => pending.Writer));
    }

    [Fact]
    public async Task AChildsPlanOrAPlanWithDocsOffOrAJobLeftUndocumentedStartsNoWriterAsync()
    {
        var off = Documented.With(enabled: false);
        var (_, offSession) = await off.RunningAsync();
        await off.PlanAsync(offSession, "Add a Webhooks container");
        var on = Documented.With();
        var (parent, _) = await on.RunningAsync();
        var (_, childSession) = await on.RunningAsync("Write the notes", parent);
        var (undocumented, undocumentedSession) = await on.RunningAsync("Fix a typo");
        await on.Book.ChooseAsync(undocumented, FeatureChoice.Undocumented, Cancellation);

        await on.PlanAsync(childSession, "Write the notes");
        await on.PlanAsync(undocumentedSession, "Fix the typo");

        Assert.Empty(off.Jobs.Submitted);
        Assert.Empty(on.Jobs.Submitted);
    }

    [Fact]
    public async Task AJobReadyForReviewGetsAsBuiltAfterItsLastVersionAndAJobWithNoVersionGetsNoneAsync()
    {
        var documented = Documented.With();
        var (job, session) = await documented.RunningAsync();
        var (plain, _) = await documented.RunningAsync("Fix a typo");
        await documented.PlanAsync(session, "Add a Webhooks container");
        var writer = documented.LastWriter;
        documented.Files.Draft(FakeJobs.Worktree(writer), DocumentationDraft.Path, "# Handle Stripe refund webhooks\n\nThe C4.\n");
        await documented.ProgressAsync(job, JobStatus.AwaitingReview);
        await documented.ProgressAsync(plain, JobStatus.AwaitingReview);

        await documented.ProgressAsync(writer, JobStatus.AwaitingReview);

        Assert.Equal([(1, VersionKind.Proposed), (2, VersionKind.AsBuilt)], documented.Published<VersionWritten>().Select(written => (written.Version.Number, written.Version.Kind)));
        Assert.All(documented.Published<VersionWritten>(), written => Assert.Equal(job, written.Job));
        Assert.Contains("- Files changed: 1, +84 −0\n", documented.Files[FakeJobs.Worktree(job), $"{Folder}/v2-as-built.md"]);
    }
}
