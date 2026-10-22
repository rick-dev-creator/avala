using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ReportedPullRequestTests(PublishedPlugins plugins)
{
    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static readonly (string File, string Content)[] LocalForge =
    [
        ("forges.json", """{ "pollSeconds": 60, "forges": [{ "name": "local", "forge": "simulated" }] }"""),
    ];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task APullRequestTheAgentPushedByUrlFromItsWorktreeIsAdoptedAndWatchedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "pushes-by-url");
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "ROUNDING.md"), "Round half to even.\n", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "add", "ROUNDING.md");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "-c", "user.name=Ada", "-c", "user.email=ada@localhost", "commit", "--quiet", "-m", "Round half to even");
        var push = $"git push --quiet {new Uri(run.Repository.Remote).AbsoluteUri} HEAD:fix/rounding";
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", new Uri(run.Repository.Remote).AbsoluteUri, "HEAD:fix/rounding");
        var number = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("fix/rounding", "main", "Round half to even", Cancellation);
        var progressed = run.Watch<JobProgressed>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, push, Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {ForgeTests.PullRequestLink(number)}", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.Equal(Option<int>.Some(number), run.Get<IPullRequests>().Of(job).Map(watch => watch.PullRequest.Number));
    }

    [Fact]
    public async Task APullRequestTheAgentReportedBeforeARestartIsAdoptedWhenItsJobComesBackForReviewAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "mentions");
        var progressed = run.Watch<JobProgressed>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {ForgeTests.PullRequestLink(1)}", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();
        var number = await ForgeTests.PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");
        await run.RestartAsync(() => progressed = run.Watch<JobProgressed>());

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, "Is the pull request ready?", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.Equal(Option<int>.Some(number), run.Get<IPullRequests>().Of(job).Map(watch => watch.PullRequest.Number));
    }

    [Fact]
    public async Task APullRequestLinkAnMcpToolOfTheAgentReturnsIsAdoptedAndWatchedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "reports-through-tool");
        var number = await ForgeTests.PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding");
        var progressed = run.Watch<JobProgressed>();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"{{\"number\": {number}, \"html_url\": \"{ForgeTests.PullRequestLink(number)}\"}}", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.Equal(Option<int>.Some(number), run.Get<IPullRequests>().Of(job).Map(watch => watch.PullRequest.Number));
    }

    [Fact]
    public async Task APullRequestFromAForkIsNotAdoptedEvenWhenItsHeadIsTheCommitThisClonePushedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "mentions");
        var number = await ForgeTests.PullRequestFromABranchNeverCheckedOutAsync(run, worktree, "fix/rounding", fork: "mallory");
        var progressed = run.Watch<JobProgressed>();

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Opened {ForgeTests.PullRequestLink(number)}", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.True(run.Get<IPullRequests>().Of(job).IsNone);
    }

    [Fact]
    public async Task APullRequestOnABranchTheAgentOnlyTriedToPushByUrlIsNotAdoptedWhenItsWorktreeOnceCheckedOutItsHeadAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "pushes-by-url");
        var url = new Uri(run.Repository.Remote).AbsoluteUri;
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "COLLEAGUE.md"), "A colleague's work.\n", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "add", "COLLEAGUE.md");
        var tree = await run.Repository.GitInAsync(worktree.Path, Cancellation, "write-tree");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "reset", "--quiet");
        File.Delete(Path.Combine(worktree.Path, "COLLEAGUE.md"));
        var colleague = await run.Repository.GitInAsync(worktree.Path, Cancellation, "-c", "user.name=Grace", "-c", "user.email=grace@localhost", "commit-tree", tree, "-p", "origin/main", "-m", "A colleague's work");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", url, $"{colleague}:refs/heads/colleague/feature");
        var number = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("colleague/feature", "main", "A colleague's work", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", "--detach", colleague);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", worktree.Branch);
        var progressed = run.Watch<JobProgressed>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"git push {url} HEAD:colleague/feature || echo rejected", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"My push was rejected, so I left {ForgeTests.PullRequestLink(number)} alone.", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.True(run.Get<IPullRequests>().Of(job).IsNone);
    }

    [Fact]
    public async Task APullRequestWhoseHeadTheAgentPushedByUrlIsNotAdoptedWhenItsWorktreeOnlyCheckedThatCommitOutAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, LocalForge, [(".avala/permissions.json", Autonomous)]);
        _ = await run.Repository.PublishAsync(Cancellation);
        var (job, worktree) = await ForgeTests.ReviewedEditAsync(run, "pushes-by-url");
        var url = new Uri(run.Repository.Remote).AbsoluteUri;
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "COLLEAGUE.md"), "A colleague's work.\n", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "add", "COLLEAGUE.md");
        var tree = await run.Repository.GitInAsync(worktree.Path, Cancellation, "write-tree");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "reset", "--quiet");
        File.Delete(Path.Combine(worktree.Path, "COLLEAGUE.md"));
        var colleague = await run.Repository.GitInAsync(worktree.Path, Cancellation, "-c", "user.name=Grace", "-c", "user.email=grace@localhost", "commit-tree", tree, "-p", "origin/main", "-m", "A colleague's work");
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "push", "--quiet", url, $"{colleague}:refs/heads/colleague/feature");
        var number = await new SimulatedForgeRemote(run.Repository.Remote).OpenPullRequestAsync("colleague/feature", "main", "A colleague's work", Cancellation);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", "--detach", colleague);
        _ = await run.Repository.GitInAsync(worktree.Path, Cancellation, "checkout", "--quiet", worktree.Branch);
        var progressed = run.Watch<JobProgressed>();
        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"git push {url} HEAD:colleague/feature", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);

        _ = Outcomes.Succeeds(await run.Get<IJobs>().SendBackAsync(job, $"Pushed to {ForgeTests.PullRequestLink(number)}.", Cancellation));
        _ = await progressed.UntilAsync(found => found.Job == job && found.Status == JobStatus.AwaitingReview);
        await run.DeliveredAsync();

        Assert.True(run.Get<IPullRequests>().Of(job).IsNone);
    }
}
