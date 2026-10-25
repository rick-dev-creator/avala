using Avala.Fleet.Contracts;
using Avala.Fleet.Recording;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Fleet.Tests;

public sealed class FleetPluginTests
{
    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task EveryHandlerIsSubscribedToEachEventItHandlesAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Empty(composition.EventsHandledButNotSubscribed());
    }

    [Fact]
    public async Task OneRecorderSeesEveryEventInOneMailboxAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Same(composition.Get<FleetRecorder>(), Assert.Single(composition.All<IHandle<JobProgressed>>()));
        Assert.Same(composition.Get<FleetRecorder>(), Assert.Single(composition.All<IHandle<Avala.Agents.Contracts.AgentActivity>>()));
    }

    [Fact]
    public async Task NoKeychainIsOfferedToOtherModulesAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Empty(composition.All<IKeychain>());
    }

    private static PluginComposition Compose(TemporaryFolder data) =>
        PluginComposition.Of(new FleetPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<TimeProvider>(new FakeTimeProvider())
            .AddSingleton<IUiDispatcher>(new TestUiDispatcher())
            .AddKeyedSingleton<IKeychain>(FleetTransport.Handler, new FakeKeychain())
            .AddSingleton<IJobCatalog>(new EmptyCatalog())
            .AddSingleton<IScratchRepositories>(new NoScratch())
            .AddSingleton<IRepositoryOrigins>(new NoOrigins()));

    private sealed class EmptyCatalog : IJobCatalog
    {
        public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

        public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobHistory>.None);

        public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

        public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);
    }

    private sealed class NoScratch : IScratchRepositories
    {
        public string Folder => string.Empty;

        public bool Holds(string repository) => false;

        public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Result<string, WorkspaceFailure>>(WorkspaceFailure.FolderMissing);
    }

    private sealed class NoOrigins : IRepositoryOrigins
    {
        public ValueTask<Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>> RemotesAsync(string repository, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>.Success([]));

        public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> RootCommitsAsync(string repository, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<IReadOnlyList<string>, WorkspaceFailure>.Success([]));
    }
}
