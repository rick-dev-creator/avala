using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ResourceTests(PublishedPlugins plugins)
{
    private const string ServicesPolicy = """{ "rules": [ { "name": "services", "kind": "command", "target": "dotnet *", "answer": "allow" } ] }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProcessLeftRunningByADiscardedJobIsReportedWithItsJobAndReapedAsync()
    {
        await using var run = await ProcessesAsync();
        var port = ServedPort(await run.TurnAsync());
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        var report = await run.OrphansFoundAsync();

        var server = Assert.Single(report.Processes);
        Assert.Equal((Option<JobId>.Some(run.Job), OrphanDisposal.Killed), (report.Job, report.Disposal));
        Assert.Contains(port, server.Ports);
        Assert.Empty(report.Survivors);
        Assert.True(await Workloads.IsGoneAsync(server.Id), $"Process {server.Id} survived its reaping");
        Assert.Equal([report], run.Get<IOrphans>().OfJob(run.Job));
    }

    [Fact]
    public async Task TheWorktreesPortLeaseReachesTheProcessesOfItsAgentAsync()
    {
        await using var run = await ProcessesAsync();

        var lease = await run.LeasedAsync();
        var port = ServedPort(await run.TurnAsync());

        Assert.Equal(lease.First, port);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(run.Worktree)), lease.Worktree);
        Assert.Equal([lease], run.Get<IResources>().Leases());
    }

    [Fact]
    public async Task TheResourcesOfAJobsProcessesAreAttributedToItsJobAndCountedGloballyAsync()
    {
        await using var run = await ProcessesAsync(("resources.json", """{ "sampleSeconds": 0.2, "diskSeconds": 0.2 }"""));
        var port = ServedPort(await run.TurnAsync());

        var sample = await run.SampledAsync(sampled => sampled.Trees.Any(tree => tree.Job == Option<JobId>.Some(run.Job) && tree.Processes.Count > 0));
        var resources = run.Get<IResources>();
        var job = resources.OfJob(run.Job);

        Assert.True(job.Processes >= 1 && job.MemoryBytes > 0, $"{job.Processes} processes using {job.MemoryBytes} bytes");
        Assert.Contains(port, job.Ports);
        Assert.True(job.DiskBytes > 0 && sample.DataFolderBytes >= job.DiskBytes, $"{job.DiskBytes} bytes in the worktree, {sample.DataFolderBytes} in the data folder");
        Assert.True(resources.Global().Processes >= job.Processes);
        Assert.Equal("simulator", Assert.Single(resources.ByConnection(), used => used.Usage.Processes > 0).Connection.Value);
        Assert.Equal("simulator", Assert.Single(resources.ByProvider(), used => used.Usage.Processes > 0).Provider);
    }

    [Fact]
    public async Task ADiscardedJobsWorktreeIsReclaimedAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        var reclaimed = await run.ReclaimedAsync();

        Assert.Equal((run.Job, JobStatus.Discarded), (reclaimed.Job, reclaimed.Status));
        Assert.False(Directory.Exists(worktree));
        Assert.Equal([reclaimed], run.Get<IWorktreeHousekeeping>().Reclaimed());
    }

    [Fact]
    public async Task AJobBeyondTheRunningLimitWaitsForASlotAndRunsOnceOneFreesAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            SimulatedRun.Simulate("hang"),
            [("budgets.json", """{ "runningJobs": 1 }""")],
            []);
        _ = await run.OpenedAsync();

        var waiting = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, SimulatedRun.Simulate("reply"))));
        var queued = await run.QueuedAsync();
        Assert.Equal(new JobQueued(waiting, 1, 1), queued);
        Assert.Single(Directory.GetDirectories(Path.Combine(run.DataFolder, "worktrees")));

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));

        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(waiting));
    }

    private Task<SimulatedRun> ProcessesAsync(params (string File, string Content)[] settings) =>
        SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("processes"), settings, [(".avala/permissions.json", ServicesPolicy)]);

    private static int ServedPort(IReadOnlyList<IAgentEvent> turn) =>
        int.Parse(
            Assert.Single(turn.OfType<ItemProgressed>(), progressed => progressed.Item.Value == "serve").Text["listening ".Length..],
            CultureInfo.InvariantCulture);
}
