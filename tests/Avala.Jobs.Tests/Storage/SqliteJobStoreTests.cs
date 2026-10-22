using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Storage;
using Avala.Jobs.Tests.Jobs;
using Avala.Sdk;
using Avala.Testing;
using AttemptOutcome = Avala.Jobs.Contracts.AttemptOutcome;

namespace Avala.Jobs.Tests.Storage;

public sealed class SqliteJobStoreTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobSurvivesAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        var job = Given.JobIn(JobState.Checking);
        Outcomes.Succeeds(job.Retry(Given.Feedback));
        Outcomes.Succeeds(job.RecordResume(Given.Session, new ResumeToken("conversation-1")));
        await SaveAsync(folder, job);

        var reloaded = await ReloadAsync(folder, store => store.FindAsync(job.Id, Cancellation));

        Assert.Equal(
            (job.Id, job.State, job.Instruction, job.Budget, job.Repository, job.Workspace, job.Session, job.Resume, job.Autonomy, job.Connection, job.Submitted, job.Parent),
            (reloaded.Id, reloaded.State, reloaded.Instruction, reloaded.Budget, reloaded.Repository, reloaded.Workspace, reloaded.Session, reloaded.Resume, reloaded.Autonomy, reloaded.Connection, reloaded.Submitted, reloaded.Parent));
        Assert.Equal(Option<ConnectionName>.Some(Given.Connection), reloaded.Connection);
        Assert.Equal(Option<JobId>.Some(Given.Parent), reloaded.Parent);
        Assert.Equal(Given.Model, reloaded.Model);
        Assert.Equal(
            job.Attempts.Select(attempt => (attempt.Number, attempt.Origin, attempt.Outcome, attempt.Guidance, attempt.Session)),
            reloaded.Attempts.Select(attempt => (attempt.Number, attempt.Origin, attempt.Outcome, attempt.Guidance, attempt.Session)));
        Assert.All(reloaded.Attempts, attempt => Assert.Equal(Option<SessionId>.Some(Given.Session), attempt.Session));
    }

    [Fact]
    public async Task TheAttachmentsOfAJobsInstructionAndOfItsLaterMessagesSurviveAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        Attachment[] screenshot = [new("screen.png", "image/png", "/data/attachments/1/screen.png")];
        Attachment[] log = [new("build.log", "text/plain", "/data/attachments/2/build.log")];
        var job = Outcomes.Succeeds(Job.Create(JobId.New(), Given.Instruction, Outcomes.Succeeds(AttemptBudget.Create(3)), Given.Repository, Given.Submitted, attachments: Option<IReadOnlyList<Attachment>>.Some(screenshot)));
        Outcomes.Succeeds(job.Submit());
        Outcomes.Succeeds(job.Start(Given.Workspace, Given.Session, Given.Connection));
        Outcomes.Succeeds(job.CompleteTurn());
        Outcomes.Succeeds(job.Pass());
        Outcomes.Succeeds(job.SendBack(Given.Feedback.With(log)));
        await SaveAsync(folder, job);

        var reloaded = await ReloadAsync(folder, store => store.FindAsync(job.Id, Cancellation));

        Assert.Equal(screenshot, reloaded.Attachments);
        Assert.Equal([screenshot, log], reloaded.Attempts.Select(attempt => attempt.Attachments.ToArray()));
    }

    [Fact]
    public async Task WhoWroteTheMessageOfEachAttemptSurvivesAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        var job = Given.JobIn(JobState.Checking);
        Outcomes.Succeeds(job.Retry(Given.Feedback.By(MessageAuthor.Avala)));
        Outcomes.Succeeds(job.CompleteTurn());
        Outcomes.Succeeds(job.Pass());
        Outcomes.Succeeds(job.SendBack(Given.Feedback));
        await SaveAsync(folder, job);

        var reloaded = await ReloadAsync(folder, store => store.FindAsync(job.Id, Cancellation));

        Assert.Equal([MessageAuthor.Person, MessageAuthor.Avala, MessageAuthor.Person], reloaded.Attempts.Select(attempt => attempt.Author));
    }

    [Fact]
    public async Task WhyAJobWasHeldSurvivesAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        var held = Given.JobIn(JobState.Running);
        Outcomes.Succeeds(held.Hold(HoldReason.BudgetExceeded));
        var running = Given.JobIn(JobState.Running);
        var unstarted = Given.JobIn(JobState.Preparing);
        Outcomes.Succeeds(unstarted.Prepared(Given.Workspace));
        Outcomes.Succeeds(unstarted.CouldNotStart("The file is locked."));
        await SaveAsync(folder, held, running, unstarted);

        var reloaded = (await ReloadAsync(folder, store => store.FindAsync(held.Id, Cancellation)), await ReloadAsync(folder, store => store.FindAsync(running.Id, Cancellation)));
        var stuck = await ReloadAsync(folder, store => store.FindAsync(unstarted.Id, Cancellation));

        Assert.Equal((Option<HoldReason>.Some(HoldReason.BudgetExceeded), Option<HoldReason>.None), (reloaded.Item1.Held, reloaded.Item2.Held));
        Assert.Equal((Option<string>.None, Option<string>.None), (reloaded.Item1.Problem, reloaded.Item2.Problem));
        Assert.Equal(
            (JobState.NeedsHelp, Option<HoldReason>.Some(HoldReason.CouldNotStart), Option<string>.Some("The file is locked."), Option<Avala.Workspaces.Contracts.WorkspaceId>.Some(Given.Workspace)),
            (stuck.State, stuck.Held, stuck.Problem, stuck.Workspace));
    }

    [Fact]
    public async Task WhenAJobEndedSurvivesAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        var ended = Given.JobIn(JobState.Discarded);
        var running = Given.JobIn(JobState.Running);
        await SaveAsync(folder, ended, running);

        var reloaded = (await ReloadAsync(folder, store => store.FindAsync(ended.Id, Cancellation)), await ReloadAsync(folder, store => store.FindAsync(running.Id, Cancellation)));

        Assert.Equal((Option<DateTimeOffset>.Some(Given.Ended), Option<DateTimeOffset>.None), (reloaded.Item1.Ended, reloaded.Item2.Ended));
    }

    [Fact]
    public async Task TheLatestConnectionChoiceOfAJobSurvivesAReloadWithItsReasonAndComparedReadingsAsync()
    {
        using var folder = new TemporaryFolder();
        var job = JobId.New();
        var at = new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero);
        var limit = new Agents.Contracts.Events.UsageLimit("5h", 0.95, at.AddHours(2));
        var earlier = new ConnectionChoice(new ConnectionName("work"), ChoiceReason.MostCapacity, [], at);
        var latest = new ConnectionChoice(
            new ConnectionName("personal"),
            ChoiceReason.AllAtLimit,
            [new CandidateCapacity(new ConnectionName("work"), 0.95, limit, 0.9, Available: false), new CandidateCapacity(new ConnectionName("personal"), 0.92, Option<Agents.Contracts.Events.UsageLimit>.None, 1, Available: false)],
            at.AddMinutes(1));
        await using (var store = new SqliteJobStore(new AvalaPaths(folder.Path)))
        {
            await store.RecordAsync(job, earlier, Cancellation);
            await store.RecordAsync(job, latest, Cancellation);
        }

        var reloaded = await ReloadAsync(folder, store => store.ChoiceOfAsync(job, Cancellation));

        Assert.Equal(latest.Compared, reloaded.Compared);
        Assert.Equal(latest with { Compared = reloaded.Compared }, reloaded);
    }

    [Fact]
    public async Task ASnapshotIsTheStoredJobAndNeverTheOneTheFlowIsChangingAsync()
    {
        using var folder = new TemporaryFolder();
        var job = Given.JobIn(JobState.Running);
        await using var store = new SqliteJobStore(new AvalaPaths(folder.Path));
        await store.SaveAsync(job, Cancellation);
        Outcomes.Succeeds(job.CompleteTurn());

        var snapshot = Outcomes.Present(await store.SnapshotAsync(job.Id, Cancellation));
        var listed = Assert.Single(await store.SnapshotsAsync(Cancellation));

        Assert.False(ReferenceEquals(job, snapshot) || ReferenceEquals(job, listed), "A snapshot is the job the flow is changing");
        Assert.Equal((JobState.Running, AttemptOutcome.Running), (snapshot.State, snapshot.Attempts[^1].Outcome));
        Assert.Equal(JobState.Running, listed.State);
        Assert.Equal(Option<Job>.None, await store.SnapshotAsync(JobId.New(), Cancellation));
    }

    [Fact]
    public async Task AJobIsFoundByItsSessionAfterAReloadAsync()
    {
        using var folder = new TemporaryFolder();
        var job = Given.JobIn(JobState.Running);
        await SaveAsync(folder, job);

        await using var store = new SqliteJobStore(new AvalaPaths(folder.Path));

        Assert.Equal(Option<JobId>.Some(job.Id), await store.JobOfSessionAsync(Given.Session, Cancellation));
        Assert.Equal(Option<JobId>.None, await store.JobOfSessionAsync(SessionId.New(), Cancellation));
    }

    [Fact]
    public async Task SavingAJobLeavesTheUnsavedChangesOfAnotherJobUnstoredAsync()
    {
        using var folder = new TemporaryFolder();
        var saved = Given.JobIn(JobState.Running);
        var changing = Given.JobIn(JobState.Running);
        await using (var store = new SqliteJobStore(new AvalaPaths(folder.Path)))
        {
            await store.SaveAsync(saved, Cancellation);
            await store.SaveAsync(changing, Cancellation);
            Outcomes.Succeeds(changing.CompleteTurn());
            Outcomes.Succeeds(saved.CompleteTurn());
            await store.SaveAsync(saved, Cancellation);
        }

        Assert.Equal(JobState.Checking, (await ReloadAsync(folder, store => store.FindAsync(saved.Id, Cancellation))).State);
        Assert.Equal(JobState.Running, (await ReloadAsync(folder, store => store.FindAsync(changing.Id, Cancellation))).State);
    }

    [Fact]
    public async Task OnlyPreparingRunningAndCheckingJobsAreActiveAsync()
    {
        using var folder = new TemporaryFolder();
        var states = new[] { JobState.Draft, JobState.Preparing, JobState.Running, JobState.Checking, JobState.AwaitingReview, JobState.Failed };
        var jobs = states.Select(state => Given.JobIn(state)).ToList();
        await SaveAsync(folder, [.. jobs]);

        await using var store = new SqliteJobStore(new AvalaPaths(folder.Path));
        var active = await store.ActiveAsync(Cancellation);

        Assert.Equal(
            [JobState.Preparing, JobState.Running, JobState.Checking],
            active.Select(job => job.State).Order());
    }

    [Fact]
    public async Task SavingAgainKeepsOneJobWithItsNewAttemptsAsync()
    {
        using var folder = new TemporaryFolder();
        var job = Given.JobIn(JobState.Running);
        await using (var store = new SqliteJobStore(new AvalaPaths(folder.Path)))
        {
            await store.SaveAsync(job, Cancellation);
            Outcomes.Succeeds(job.CompleteTurn());
            Outcomes.Succeeds(job.Retry(Given.Feedback));
            await store.SaveAsync(job, Cancellation);
        }

        var reloaded = await ReloadAsync(folder, store => store.FindAsync(job.Id, Cancellation));

        Assert.Equal(
            job.Attempts.Select(attempt => (attempt.Outcome, attempt.Guidance)),
            reloaded.Attempts.Select(attempt => (attempt.Outcome, attempt.Guidance)));
    }

    private static async Task SaveAsync(TemporaryFolder folder, params Job[] jobs)
    {
        await using var store = new SqliteJobStore(new AvalaPaths(folder.Path));

        foreach (var job in jobs)
        {
            await store.SaveAsync(job, Cancellation);
        }
    }

    private static async Task<T> ReloadAsync<T>(TemporaryFolder folder, Func<SqliteJobStore, Task<Option<T>>> find)
        where T : notnull
    {
        await using var store = new SqliteJobStore(new AvalaPaths(folder.Path));

        return Outcomes.Present(await find(store));
    }
}
