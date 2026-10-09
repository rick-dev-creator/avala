using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Jobs;
using Avala.Jobs.Storage;
using Avala.Jobs.Tests.Jobs;
using Avala.Sdk;
using Avala.Testing;

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
            (job.Id, job.State, job.Instruction, job.Budget, job.Repository, job.Workspace, job.Session, job.Resume, job.Autonomy, job.Connection),
            (reloaded.Id, reloaded.State, reloaded.Instruction, reloaded.Budget, reloaded.Repository, reloaded.Workspace, reloaded.Session, reloaded.Resume, reloaded.Autonomy, reloaded.Connection));
        Assert.Equal(Option<ConnectionName>.Some(Given.Connection), reloaded.Connection);
        Assert.Equal(
            job.Attempts.Select(attempt => (attempt.Number, attempt.Origin, attempt.Outcome, attempt.Guidance)),
            reloaded.Attempts.Select(attempt => (attempt.Number, attempt.Origin, attempt.Outcome, attempt.Guidance)));
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

    private static async Task<Job> ReloadAsync(TemporaryFolder folder, Func<SqliteJobStore, Task<Option<Job>>> find)
    {
        await using var store = new SqliteJobStore(new AvalaPaths(folder.Path));

        return (await find(store)).Match(job => job, () => throw new InvalidOperationException("The job was not stored"));
    }
}
