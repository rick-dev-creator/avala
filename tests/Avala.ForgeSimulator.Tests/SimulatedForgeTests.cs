using Avala.ForgeSimulator.Remote;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
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
        await using var forge = new SimulatedForge(Processes);
        var context = await PublishedAsync(repository, "green");

        Assert.Empty(await ForgeConformance.CheckAsync(new ConformanceCase(forge, context, Head, "main"), Cancellation));
    }

    [Fact]
    public async Task AScenarioThatFailsOnceFailsTheFirstHeadAndPassesTheNextPushedOneAsync()
    {
        await using var repository = await TemporaryRepository.CreateAsync(Processes, Cancellation);
        await using var forge = new SimulatedForge(Processes);
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
        await using var forge = new SimulatedForge(Processes);
        var context = await PublishedAsync(repository, "green");
        var opened = Outcomes.Succeeds(await forge.OpenAsync(context, new PullRequestDraft(Head, "main", "Greet", "Body"), Cancellation));

        _ = Outcomes.Succeeds(await forge.CommentAsync(context, opened, "A person needs to look.", Cancellation));

        Assert.Contains("A person needs to look.", await File.ReadAllTextAsync(Path.Combine(repository.Remote, SimulatedForge.StateFile), Cancellation), StringComparison.Ordinal);
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
