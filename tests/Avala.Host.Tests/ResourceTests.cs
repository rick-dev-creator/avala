using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Transcripts.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Host.Tests;

public sealed class ResourceTests(PublishedPlugins plugins)
{
    private const string ServicesPolicy = """{ "rules": [ { "name": "services", "kind": "command", "target": "dotnet *", "answer": "allow" } ] }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AProcessAnEarlierRunLeftInATreeItRecordedIsReapedAtStartupEvenIfItIgnoresBeingAskedToEndAndAProcessAvalaNeverStartedIsLeftAloneAsync()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "On Windows a tree is a job object that kills its processes when the run that held it ends.");
        var earlier = ProcessTreeId.New();
        using var left = await HoldingAsync("stubborn", Option<ProcessTreeId>.Some(earlier));
        using var stranger = await HoldingAsync("hold", Option<ProcessTreeId>.None);

        await using var run = await SimulatedRun.PreparedAsync(plugins, [(TreeLedgerFile, $"[\"{earlier.Value}\"]")], []);
        await run.StartedAsync();
        await left.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromSeconds(30), Cancellation);

        Assert.False(stranger.HasExited);
        stranger.Kill();
    }

    private const string TreeLedgerFile = "process-trees.json";

    private async Task<System.Diagnostics.Process> HoldingAsync(string mode, Option<ProcessTreeId> tree)
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
        };
        start.ArgumentList.Add(Path.Combine(plugins.Directory, "Avala.Simulator", "Avala.Simulator.Workload.dll"));
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        start.Environment.Remove(ProcessTreeId.Variable);

        foreach (var id in tree.Match<ProcessTreeId[]>(found => [found], () => []))
        {
            start.Environment[ProcessTreeId.Variable] = id.Value.ToString();
        }

        var process = System.Diagnostics.Process.Start(start)!;
        Assert.NotNull(await process.StandardOutput.ReadLineAsync(Cancellation));

        return process;
    }

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
    public async Task AProcessOfAJobWhoseIdleAgentEndedKeepsRunningUntilTheJobEndsAndIsReapedThenAsync()
    {
        await using var run = await ProcessesAsync();
        var stopped = run.Watch<SessionStopped>();
        var port = ServedPort(await run.TurnAsync());
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        await run.DeliveredAsync();
        run.Clock.Advance(TimeSpan.FromHours(1));
        _ = await stopped.UntilAsync(_ => true);
        await run.DeliveredAsync();
        Assert.Empty(run.Get<IOrphans>().OfJob(run.Job));

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        var report = await run.OrphansFoundAsync();

        var server = Assert.Single(report.Processes);
        Assert.Equal((Option<JobId>.Some(run.Job), OrphanDisposal.Killed), (report.Job, report.Disposal));
        Assert.Contains(port, server.Ports);
        Assert.True(await Workloads.IsGoneAsync(server.Id), $"Process {server.Id} survived its reaping");
    }

    [Fact]
    public async Task TheWorktreesPortLeaseReachesTheProcessesOfItsAgentAsync()
    {
        await using var run = await ProcessesAsync();

        var lease = await run.LeasedAsync();
        var port = ServedPort(await run.TurnAsync());

        Assert.Equal(lease.First, port);
        Assert.Equal(run.Worktree.Canonical(), lease.Worktree);
        Assert.Equal([lease], run.Get<IResources>().Leases());
    }

    [Fact]
    public async Task TheResourcesOfAJobsProcessesAreAttributedToItsJobAndCountedGloballyAsync()
    {
        var sampling = TimeSpan.FromSeconds(0.2);
        await using var run = await ProcessesAsync(""" "sampleSeconds": 0.2, "diskSeconds": 0.2 """);
        await run.StartedAsync();
        var port = ServedPort(await run.TurnAsync());
        await run.DeliveredAsync();

        run.Clock.Advance(sampling);

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
    public async Task ADiscardedJobsWorktreeIsFreedAtOnceAndItsWorkKeptAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        var freed = await run.FreedAsync();

        Assert.Equal((run.Job, JobStatus.Discarded, WorktreeMoment.Discarded), (freed.Job, freed.Status, freed.Moment));
        Assert.False(Directory.Exists(worktree));
        Assert.True(Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAtAsync(worktree, Cancellation)).Shelved);
        Assert.Equal([freed], run.Get<IWorktreeHousekeeping>().Freed());
    }

    [Fact]
    public async Task ADiscardedJobsWorkDueForDeletionAtOnceIsReclaimedAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "worktrees": { "deleteDiscardedHours": 0 } }""")], []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;

        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        var reclaimed = await run.ReclaimedAsync();

        Assert.Equal((run.Job, JobStatus.Discarded), (reclaimed.Job, reclaimed.Status));
        Assert.False(Directory.Exists(worktree));
        Assert.Equal([reclaimed], run.Get<IWorktreeHousekeeping>().Reclaimed());
    }

    [Fact]
    public async Task AnApprovedJobWhoseWorktreeWasFreedContinuesInItsRebuiltWorktreeAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "worktrees": { "freeUndeliveredHours": 0 } }""")], []);
        var progressed = run.Watch<JobProgressed>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
        Assert.Equal(WorktreeMoment.Undelivered, (await run.FreedAsync()).Moment);
        Assert.False(Directory.Exists(worktree));

        var continued = Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(run.Job, "One more thing.", Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.AwaitingReview);

        Assert.Equal(ContinuedIn.ResumedConversation, continued.Conversation);
        Assert.True(Directory.Exists(worktree));
        Assert.False(Outcomes.Succeeds(await run.Get<IWorkspaces>().FindAtAsync(worktree, Cancellation)).Shelved);
    }

    [Fact]
    public async Task AJobWaitingForReviewIsFreedOnceIdleAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "worktrees": { "freeIdleHours": 1, "freeUndeliveredHours": null } }""")], []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;
        await run.DeliveredAsync();

        run.Clock.Advance(TimeSpan.FromHours(1));
        var freed = await run.FreedAsync();

        Assert.Equal((run.Job, JobStatus.AwaitingReview, WorktreeMoment.Idle), (freed.Job, freed.Status, freed.Moment));
        Assert.False(Directory.Exists(worktree));
    }

    [Fact]
    public async Task ApprovingAJobFreedWhileItWaitedRebuildsItsWorktreeAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "worktrees": { "freeIdleHours": 1, "freeUndeliveredHours": null } }""")], []);
        var progressed = run.Watch<JobProgressed>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;
        await run.DeliveredAsync();
        run.Clock.Advance(TimeSpan.FromHours(1));
        _ = await run.FreedAsync();

        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.Approved);

        Assert.True(Directory.Exists(worktree));
    }

    [Fact]
    public async Task AWorktreeOverTheSpaceBudgetIsFreedAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "diskSeconds": 0.1, "worktrees": { "freeUndeliveredHours": null, "spaceBudgetGigabytes": 0.000000001 } }""")], []);
        var progressed = run.Watch<JobProgressed>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.Approved);

        run.Clock.Advance(TimeSpan.FromSeconds(10));
        var freed = await run.FreedAsync();

        Assert.Equal((run.Job, WorktreeMoment.Space), (freed.Job, freed.Moment));
    }

    [Fact]
    public async Task AnApprovedJobWhoseWorkWasMergedIsFreedAtOnceAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [], [(".avala/jobs.json", """{ "approval": "merge" }""")]);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());

        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
        var freed = await run.FreedAsync();

        Assert.Equal((run.Job, JobStatus.Approved, WorktreeMoment.Delivered), (freed.Job, freed.Status, freed.Moment));
    }

    [Fact]
    public async Task ArchivingAnApprovedJobFreesItsWorktreeAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "reply");
        var progressed = run.Watch<JobProgressed>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.Approved);
        await run.DeliveredAsync();
        Assert.True(Directory.Exists(worktree));

        _ = Outcomes.Succeeds(await run.Get<IProjects>().ArchiveAsync([run.Job], Cancellation));
        var freed = await run.FreedAsync();

        Assert.Equal((run.Job, WorktreeMoment.Archived), (freed.Job, freed.Moment));
        Assert.False(Directory.Exists(worktree));
    }

    [Fact]
    public async Task AnApprovedJobReopenedByAMessageKeepsItsWorktreeWhenItIsDueToBeFreedAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "worktrees": { "freeUndeliveredHours": 1 } }""")], []);
        var progressed = run.Watch<JobProgressed>();
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.Approved);
        Outcomes.Succeeds(await run.Get<IJobs>().ContinueAsync(run.Job, "One more thing.", Cancellation));
        _ = await progressed.UntilAsync(update => update.Job == run.Job && update.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        var due = run.Clock.GetUtcNow() + TimeSpan.FromHours(1);
        run.Clock.Advance(TimeSpan.FromHours(1));
        _ = await run.SampledAsync(sample => sample.At >= due);
        await run.DeliveredAsync();

        Assert.True(Directory.Exists(worktree));
        Outcomes.Succeeds(await run.Get<IJobs>().ApproveAsync(run.Job, Cancellation));
    }

    [Fact]
    public async Task ARetentionPendingWhenTheApplicationStopsReclaimsTheWorktreeAndReleasesTheConversationOnceItsDeadlinePassesAfterARestartAsync()
    {
        await using var run = await SimulatedRun.InstructedAsync(plugins, SimulatedRun.Simulate("reply"), [("resources.json", """{ "worktrees": { "freeDiscardedHours": null, "deleteDiscardedHours": 1 } }""")], []);
        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        var worktree = run.Worktree;
        Outcomes.Succeeds(await run.Get<IJobs>().DiscardAsync(run.Job, Cancellation));
        await run.DeliveredAsync();

        await run.RestartAsync();
        await run.StartedAsync();
        Assert.True(Directory.Exists(worktree));
        run.Clock.Advance(TimeSpan.FromHours(1));
        var reclaimed = await run.ReclaimedAsync();
        await run.DeliveredAsync();
        await run.RestartAsync();
        await run.StartedAsync();

        Assert.Equal((run.Job, JobStatus.Discarded), (reclaimed.Job, reclaimed.Status));
        Assert.False(Directory.Exists(worktree));
        Assert.Equal([new AttemptBegan(1)], (await run.Get<ITranscripts>().EarlierRunsAsync(run.Job, Cancellation)).Select(kept => kept.Fact));
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

    private Task<SimulatedRun> ProcessesAsync(string settings = "") =>
        SimulatedRun.InstructedAsync(
            plugins,
            SimulatedRun.Simulate("processes"),
            [("resources.json", $"{{ {string.Join(", ", new[] { LeasedPorts.Settings(), settings }.Where(part => part.Length > 0))} }}")],
            [(".avala/permissions.json", ServicesPolicy)]);

    private static int ServedPort(IReadOnlyList<IAgentEvent> turn) =>
        int.Parse(
            Assert.Single(turn.OfType<ItemProgressed>(), progressed => progressed.Item.Value == "serve").Text["listening ".Length..],
            CultureInfo.InvariantCulture);
}
