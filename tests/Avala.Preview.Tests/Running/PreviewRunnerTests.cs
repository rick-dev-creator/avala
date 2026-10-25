using System.Collections.Immutable;
using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Running;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Preview.Tests.Running;

public sealed class PreviewRunnerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Harness => Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task RunStartsTheJobsScriptInItsWorktreeOnItsLeasedPortAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("hold", Harness), RunBench.Leased);

        var run = Outcomes.Succeeds(await bench.Previews.RunAsync(bench.Job, Cancellation));

        var tree = Assert.Single(bench.Trees.Opened);
        var started = Assert.Single(tree.Requests);
        Assert.Equal(new PreviewRun(bench.Job, "dev"), run);
        Assert.Equal(bench.Worktree.Path, tree.Home);
        Assert.Equal(bench.Worktree.Path, started.WorkingDirectory);
        Assert.Equal(("24000", "127.0.0.1", "none"), (started.Environment["PORT"], started.Environment["HOST"], started.Environment["BROWSER"]));
        Assert.True(bench.Previews.IsRunning(bench.Job));
        Assert.Equal(Option<JobId>.Some(bench.Job), bench.Runs.OwnerOf(tree.Id));
        Assert.Equal(new PreviewRunChanged(bench.Job, Running: true), Assert.Single(bench.Bus.Published.OfType<PreviewRunChanged>()));
    }

    [Fact]
    public async Task ADocsSiteIsServedByDocfxOnTheJobsLeasedPortAsync()
    {
        await using var bench = new RunBench(Option<Launch>.None, RunBench.Leased);

        var served = await bench.Previews.RunSiteAsync(bench.Job, "docs/docfx.json", Cancellation);

        var started = Assert.Single(Assert.Single(bench.Trees.Opened).Requests);
        Assert.Equal(OperatingSystem.IsWindows() ? "docfx.exe" : "docfx", started.FileName);
        Assert.Equal(["docs/docfx.json", "--serve", "--hostname", "127.0.0.1", "--port", "24000"], started.ArgumentList);
        Assert.Equal(bench.Worktree.Path, started.WorkingDirectory);
        Assert.True(served.IsSuccess || Outcomes.FailsWith(served) == PreviewError.NotStarted);
    }

    [Fact]
    public async Task ARunEndsAndClosesItsTreeOnceItsScriptExitsAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("work", "0"), RunBench.Leased);

        _ = Outcomes.Succeeds(await bench.Previews.RunAsync(bench.Job, Cancellation));
        await bench.Bus.WaitForAsync<PreviewRunChanged>(changed => !changed.Running, Cancellation);

        Assert.False(bench.Previews.IsRunning(bench.Job));
        Assert.Equal(Assert.Single(bench.Trees.Opened).Id, Assert.Single(bench.Trees.Closed));
    }

    [Fact]
    public async Task RunIsRefusedWhenTheWorktreeHasNoScriptAndNoPageAsync()
    {
        await using var bench = new RunBench(Option<Launch>.None, RunBench.Leased);

        Assert.Equal(PreviewError.NoScript, Outcomes.FailsWith(await bench.Previews.RunAsync(bench.Job, Cancellation)));
        Assert.Empty(bench.Trees.Opened);
        Assert.Equal(Option<string>.None, await bench.Previews.ScriptAsync(bench.Job, Cancellation));
    }

    [Fact]
    public async Task RunIsRefusedWhenTheWorktreeHasNoLeasedPortsAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("hold", Harness), ImmutableDictionary<string, string>.Empty);

        Assert.Equal(PreviewError.NoLease, Outcomes.FailsWith(await bench.Previews.RunAsync(bench.Job, Cancellation)));
        var tree = Assert.Single(bench.Trees.Opened);
        Assert.Empty(tree.Requests);
        Assert.Equal([tree.Id], bench.Trees.Closed);
        Assert.False(bench.Previews.IsRunning(bench.Job));
    }

    [Fact]
    public async Task RunIsRefusedForAJobWithoutAWorktreeAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("hold", Harness), RunBench.Leased);

        Assert.Equal(PreviewError.NoWorktree, Outcomes.FailsWith(await bench.Previews.RunAsync(JobId.New(), Cancellation)));
        Assert.Empty(bench.Trees.Opened);
    }

    [Fact]
    public async Task StopEndsARunningScriptAndKillsItsTreeAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("hold", Harness), RunBench.Leased);
        _ = Outcomes.Succeeds(await bench.Previews.RunAsync(bench.Job, Cancellation));
        var process = Assert.Single(Assert.Single(bench.Trees.Opened).Started);

        Assert.Equal(new PreviewRun(bench.Job, "dev"), Outcomes.Succeeds(await bench.Previews.StopAsync(bench.Job, Cancellation)));

        Assert.False(bench.Previews.IsRunning(bench.Job));
        Assert.Single(bench.Trees.Closed);
        Assert.True(await Workloads.IsGoneAsync(process), $"Process {process} survived the stop");
        Assert.Equal(PreviewError.NotRunning, Outcomes.FailsWith(await bench.Previews.StopAsync(bench.Job, Cancellation)));
    }

    [Fact]
    public async Task AJobThatEndsStopsItsRunAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("hold", Harness), RunBench.Leased);
        _ = Outcomes.Succeeds(await bench.Previews.RunAsync(bench.Job, Cancellation));

        await bench.Previews.HandleAsync(new JobProgressed(bench.Job, JobStatus.Discarded), Cancellation);

        Assert.False(bench.Previews.IsRunning(bench.Job));
        Assert.Single(bench.Trees.Closed);
        Assert.Equal([bench.Job], bench.Pages.Closed);
    }

    [Fact]
    public async Task ScriptNamesWhatRunWouldStartAsync()
    {
        await using var bench = new RunBench(RunBench.Workload("hold", Harness), RunBench.Leased);

        Assert.Equal(Option<string>.Some("dev"), await bench.Previews.ScriptAsync(bench.Job, Cancellation));
    }

    [Fact]
    public async Task APageIsServedOnItsOwnLeaseAndOfferedAsTheJobsPreviewUntilItStopsAsync()
    {
        await using var bench = new RunBench(new Launch("index.html", LaunchKind.Page, string.Empty, []), RunBench.Leased);

        Assert.Equal(new PreviewRun(bench.Job, "index.html"), Outcomes.Succeeds(await bench.Previews.RunAsync(bench.Job, Cancellation)));
        var page = new PreviewTarget(bench.Job, new Uri("http://127.0.0.1:24010/index.html"), PreviewSource.File) { Leased = true };
        Assert.Equal([page], bench.Previews.Of(bench.Job));
        Assert.Contains(new PreviewAvailable(page), bench.Bus.Published);

        _ = Outcomes.Succeeds(await bench.Previews.StopAsync(bench.Job, Cancellation));

        Assert.Empty(bench.Previews.Of(bench.Job));
        Assert.Contains(new PreviewGone(page), bench.Bus.Published);
        Assert.Equal([bench.Job], bench.Pages.Stopped);
        Assert.Empty(bench.Trees.Opened);
    }

    [Fact]
    public async Task AFileOfTheWorktreeIsServedOnTheJobsFilesGatewayAsync()
    {
        await using var bench = new RunBench(Option<Launch>.None, RunBench.Leased);

        var address = Outcomes.Succeeds(await bench.Previews.ServeFileAsync(bench.Job, Path.Combine(bench.Worktree.Path, "models", "robot.glb"), Cancellation));

        Assert.Equal(new Uri("http://127.0.0.1:24010/models/robot.glb"), address);
    }

    [Fact]
    public async Task AFileOutsideTheWorktreeIsRefusedAsync()
    {
        await using var bench = new RunBench(Option<Launch>.None, RunBench.Leased);

        var refused = await bench.Previews.ServeFileAsync(bench.Job, Path.Combine(bench.Worktree.Path, "..", "elsewhere.glb"), Cancellation);

        Assert.Equal(PreviewError.Outside, Outcomes.FailsWith(refused));
    }
}
