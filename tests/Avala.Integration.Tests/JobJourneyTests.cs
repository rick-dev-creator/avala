using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Integration.Tests;

public sealed class JobJourneyTests
{
    private const string Instruction = "Add GitHub login";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASubmittedJobAwaitsReviewWithItsAttemptCheckpointedInTheWorktreeAsync()
    {
        using var data = new TemporaryFolder();
        await using var tools = new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider();
        await using var repository = await TemporaryRepository.CreateAsync(tools.GetRequiredService<IProcessRunner>(), Cancellation);
        var agent = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        await using var avala = AvalaApplication.Compose(data.Path, agent);
        var progress = avala.Watch<JobProgressed>();
        avala.Start();

        var job = await SubmitAsync(avala, repository);

        await ReachesAsync(progress, job, JobStatus.AwaitingReview);
        var worktree = Assert.Single(agent.Sessions).Options.WorkingDirectory;
        Assert.Equal("Attempt 1", await repository.GitInAsync(worktree, Cancellation, "log", "-1", "--format=%s"));
    }

    [Fact]
    public async Task AJobInterruptedByARestartIsResumedInANewSessionAndAwaitsReviewAsync()
    {
        using var data = new TemporaryFolder();
        await using var tools = new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider();
        await using var repository = await TemporaryRepository.CreateAsync(tools.GetRequiredService<IProcessRunner>(), Cancellation);
        var silent = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]);
        var replying = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        JobId job;

        await using (var first = AvalaApplication.Compose(data.Path, silent))
        {
            var progress = first.Watch<JobProgressed>();
            var activity = first.Watch<AgentActivity>();
            first.Start();
            job = await SubmitAsync(first, repository);
            await ReachesAsync(progress, job, JobStatus.Running);
            await activity.UntilAsync(update => update.Event is TurnStarted);
        }

        await using (var second = AvalaApplication.Compose(data.Path, replying))
        {
            var progress = second.Watch<JobProgressed>();
            second.Start();
            await ReachesAsync(progress, job, JobStatus.AwaitingReview);
        }

        Assert.Equal(Assert.Single(silent.Sessions).Options.WorkingDirectory, Assert.Single(replying.Sessions).Options.WorkingDirectory);
        Assert.Equal([("Initial", "Interrupted"), ("Recovery", "Passed")], await AttemptsAsync(new AvalaPaths(data.Path)));
    }

    private static async Task<JobId> SubmitAsync(AvalaApplication avala, TemporaryRepository repository) =>
        Outcomes.Succeeds(await avala.Get<IJobs>().SubmitAsync(new JobRequest(repository.Path, Instruction), Cancellation));

    private static async Task ReachesAsync(EventWatch<JobProgressed> progress, JobId job, JobStatus status)
    {
        var reached = await progress.UntilAsync(update => update.Job == job && (update.Status == status || update.Status == JobStatus.Failed));

        Assert.Equal(status, reached.Status);
    }

    private static async Task<IReadOnlyList<(string Origin, string Outcome)>> AttemptsAsync(AvalaPaths paths)
    {
        await using var connection = new SqliteConnection($"Data Source={paths.Database("jobs")};Pooling=False");
        await connection.OpenAsync(Cancellation);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Origin, Outcome FROM JobAttempts ORDER BY Number";
        await using var reader = await command.ExecuteReaderAsync(Cancellation);
        var attempts = new List<(string Origin, string Outcome)>();

        while (await reader.ReadAsync(Cancellation))
        {
            attempts.Add((reader.GetString(0), reader.GetString(1)));
        }

        return attempts;
    }
}
