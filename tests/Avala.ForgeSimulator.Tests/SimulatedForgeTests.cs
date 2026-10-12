using Avala.ForgeSimulator.Remote;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Triggers.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.ForgeSimulator.Tests;

public sealed class SimulatedForgeTests
{
    private const string Head = "avala/job";

    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheSimulatedForgePassesTheForgeConformanceKitOnARealBareRemoteAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(Path.GetTempPath()));
        var context = await PublishedAsync(repository, "green");

        Assert.Empty(await ForgeConformance.CheckAsync(new ConformanceCase(forge, context, Head, "main"), Cancellation));
    }

    [Fact]
    public async Task TheSimulatedForgePassesTheAccountPartOfTheForgeConformanceKitAsync()
    {
        await using var data = new TemporaryFolder();
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(data.Path));

        Assert.Empty(await ForgeConformance.CheckAccountAsync(forge, Account(), Cancellation));
    }

    [Fact]
    public async Task TheSimulatedForgeListsItsBareRepositoriesMostRecentlyChangedFirstAsync()
    {
        await using var data = new TemporaryFolder();
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(data.Path));
        var older = Outcomes.Succeeds(await forge.CreateAsync(Account(), new RepositoryDraft("older", Private: true), Cancellation));
        var newer = Outcomes.Succeeds(await forge.CreateAsync(Account(), new RepositoryDraft("newer", Private: true), Cancellation));
        Directory.SetLastWriteTimeUtc(older.HttpsUrl, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(newer.HttpsUrl, new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(Path.Combine(older.HttpsUrl, "refs"), new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc));
        Directory.SetLastWriteTimeUtc(Path.Combine(newer.HttpsUrl, "refs"), new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc));
        await TouchAsync(Path.Combine(older.HttpsUrl, "refs", "heads", "main"), new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc));

        var listed = Outcomes.Succeeds(await forge.ListAsync(Account(), Cancellation));

        Assert.Equal(["older", "newer"], listed.Select(found => found.Repository.Address.Name));
    }

    [Fact]
    public async Task AScenarioThatFailsOnceFailsTheFirstHeadAndPassesTheNextPushedOneAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(Path.GetTempPath()));
        var context = await PublishedAsync(repository, "ci-fails-once");
        var opened = Outcomes.Succeeds(await forge.OpenAsync(context, new PullRequestDraft(Head, "main", "[forge: ci-fails-once] Greet", "Body"), Cancellation));

        var first = Outcomes.Succeeds(await forge.ReadAsync(context, opened, Cancellation));
        await repository.CommitToRemoteAsync(Head, "FIX.md", "fixed\n", Cancellation);
        var second = Outcomes.Succeeds(await forge.ReadAsync(context, opened, Cancellation));

        Assert.Equal(CheckStatus.Failed, Assert.Single(first.Checks).Status);
        Assert.Equal(CheckStatus.Passed, Assert.Single(second.Checks).Status);
        Assert.NotEqual(first.HeadCommit, second.HeadCommit);
    }

    [Fact]
    public async Task ACommentIsKeptWithThePullRequestInTheRemoteAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(Path.GetTempPath()));
        var context = await PublishedAsync(repository, "green");
        var opened = Outcomes.Succeeds(await forge.OpenAsync(context, new PullRequestDraft(Head, "main", "Greet", "Body"), Cancellation));

        _ = Outcomes.Succeeds(await forge.CommentAsync(context, opened, "A person needs to look.", Cancellation));

        Assert.Contains("A person needs to look.", await File.ReadAllTextAsync(Path.Combine(repository.Remote, SimulatedForge.StateFile), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EventsOfAKindAreReadOldestFirstSinceTheCursorAPageAtATimeAndTheFirstReadIsABaselineAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(Path.GetTempPath()));
        var context = await PublishedAsync(repository, "green");
        var script = new SimulatedForgeRemote(repository.Remote);
        var before = await script.OpenIssueAsync("Old", "Before Avala looked", "alice", Cancellation);
        await script.LabelAsync(before, "avala", "alice", Cancellation);

        var baseline = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, Option<string>.None), Cancellation));
        var issue = await script.OpenIssueAsync("Checkout button does nothing", "Clicking it shows nothing.", "mallory", Cancellation, pullRequest: true, fork: true);
        await script.LabelAsync(issue, "bug", "bob", Cancellation);
        await script.LabelAsync(issue, "avala", "alice", Cancellation);
        var first = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, baseline.Cursor) with { Most = 1 }, Cancellation));
        var rest = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, first.Cursor), Cancellation));
        var none = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.IssueLabeled, rest.Cursor), Cancellation));

        Assert.Empty(baseline.Events);
        var bug = Assert.Single(first.Events);
        Assert.Equal((issue, "Checkout button does nothing", "mallory", "bob", Option<string>.Some("bug"), true, true), (bug.Number, bug.Title, bug.Author, bug.Actor, bug.Label, bug.PullRequest, bug.FromFork));
        Assert.Equal(bug.Cursor, first.Cursor);
        Assert.Equal([("alice", Option<string>.Some("avala"))], rest.Events.Select(labeled => (labeled.Actor, labeled.Label)));
        Assert.Empty(none.Events);
        Assert.Equal(rest.Cursor, none.Cursor);
    }

    [Fact]
    public async Task FailedChecksOnABranchAreOneEventPerHeadAndTheHeadFailingAtTheBaselineIsNoEventAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(Path.GetTempPath()));
        var context = await PublishedAsync(repository, "green");
        var script = new SimulatedForgeRemote(repository.Remote);
        await script.FailChecksAsync(await repository.GitInAsync(repository.Remote, Cancellation, "rev-parse", "main"), "alice", Cancellation);

        var baseline = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, Option<string>.None), Cancellation));
        await repository.CommitToRemoteAsync("main", "CHANGELOG.md", "broken\n", Cancellation);
        var head = await repository.GitInAsync(repository.Remote, Cancellation, "rev-parse", "main");
        var passing = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, baseline.Cursor), Cancellation));
        await script.FailChecksAsync(head, "bob", Cancellation);
        var failed = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, passing.Cursor), Cancellation));
        var again = Outcomes.Succeeds(await forge.ReadSinceAsync(context, Query(ForgeEventKind.ChecksFailed, failed.Cursor), Cancellation));

        Assert.Empty(baseline.Events);
        Assert.Empty(passing.Events);
        var failure = Assert.Single(failed.Events);
        Assert.Equal(($"main:{head}", "bob", Option<string>.Some(head), CheckStatus.Failed), (failure.Id, failure.Actor, failure.Head, Assert.Single(failure.Checks).Status));
        Assert.Empty(again.Events);
    }

    [Fact]
    public async Task CreatingARepositoryMakesAnEmptyBareRemoteUnderTheDataFolderThatAcceptsAPushAsync()
    {
        await using var data = new TemporaryFolder();
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(data.Path));

        var created = Outcomes.Succeeds(await forge.CreateAsync(Account(), new RepositoryDraft("invoices", Private: true), Cancellation));
        await repository.GitAsync(Cancellation, "push", "--quiet", created.HttpsUrl, "main");

        Assert.Equal((new RepositoryAddress(string.Empty, "avala", "invoices"), new Uri("https://forge.invalid/avala/invoices")), (created.Address, created.Link));
        var bare = Path.Combine(data.Path, "simulated-forge", "avala", "invoices.git");
        Assert.Equal((bare, bare), (created.HttpsUrl, created.SshUrl));
        Assert.Equal(await repository.GitAsync(Cancellation, "rev-parse", "main"), await repository.GitInAsync(created.SshUrl, Cancellation, "rev-parse", "main"));
    }

    [Theory]
    [InlineData("invoices", ForgeError.NameTaken)]
    [InlineData("code/invoices", ForgeError.Rejected)]
    public async Task ARepositoryNeedsAPlainNameNotTakenYetAsync(string name, ForgeError expected)
    {
        await using var data = new TemporaryFolder();
        await using var forge = new SimulatedForge(Processes, new AvalaPaths(data.Path));
        _ = Outcomes.Succeeds(await forge.CreateAsync(Account(), new RepositoryDraft("invoices", Private: true), Cancellation));

        Assert.Equal(expected, Outcomes.FailsWith(await forge.CreateAsync(Account(), new RepositoryDraft(name, Private: true), Cancellation)));
    }

    private static ForgeContext Account() =>
        new(new Unused(), new ForgeTarget(SimulatedForge.Site, new RepositoryAddress(string.Empty, string.Empty, string.Empty), string.Empty));

    private static ForgeEventQuery Query(ForgeEventKind kind, Option<string> cursor) =>
        new(kind, cursor, DateTimeOffset.UnixEpoch) { Branch = "main" };

    private static async Task TouchAsync(string file, DateTime at)
    {
        await File.WriteAllTextAsync(file, "0000000000000000000000000000000000000000\n", Cancellation);
        File.SetLastWriteTimeUtc(file, at);
    }

    private static async Task<ForgeContext> PublishedAsync(TemporaryRepository repository, string scenario)
    {
        var remote = await repository.PublishAsync(Cancellation);
        await repository.CommitToRemoteAsync(Head, "GREETING.md", $"# Hello {scenario}\n", Cancellation);

        return new ForgeContext(new Unused(), new ForgeTarget(SimulatedForge.Site, new RepositoryAddress(string.Empty, "octo", "shop"), remote));
    }

    private sealed class Unused : IForgeApi
    {
        public ValueTask<Result<ForgeResponse, ForgeError>> SendAsync(ForgeRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ForgeResponse, ForgeError>.Failure(ForgeError.Unreachable));
    }
}
