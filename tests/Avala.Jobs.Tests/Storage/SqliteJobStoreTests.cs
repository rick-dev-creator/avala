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
        await SaveAsync(folder, job);

        var reloaded = await ReloadAsync(folder, store => store.FindAsync(job.Id, Cancellation));

        Assert.Equal(
            (job.Id, job.State, job.Instruction, job.Budget, job.Repository, job.Workspace, job.Session),
            (reloaded.Id, reloaded.State, reloaded.Instruction, reloaded.Budget, reloaded.Repository, reloaded.Workspace, reloaded.Session));
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

        var reloaded = await ReloadAsync(folder, store => store.FindBySessionAsync(Given.Session, Cancellation));

        Assert.Equal(job.Id, reloaded.Id);
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

        Assert.Equal(2, reloaded.Attempts.Count);
        Assert.Equal(Option<Feedback>.Some(Given.Feedback), reloaded.Attempts[^1].Guidance);
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
