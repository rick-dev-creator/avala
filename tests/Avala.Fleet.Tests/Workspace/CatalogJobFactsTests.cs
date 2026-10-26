using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Fleet.Recording;
using Avala.Fleet.Workspace;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Fleet.Tests.Workspace;

public sealed class CatalogJobFactsTests
{
    private const string Repository = "/home/rick/avala";

    private static readonly DateTimeOffset Submitted = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly JobId Job = new(Guid.Parse("0199f6a0-0000-7000-8000-000000000100"));

    private static readonly JobId Parent = new(Guid.Parse("0199f6a0-0000-7000-8000-000000000200"));

    private static readonly OriginRemote Origin = new("origin", "git@github.com:Rick/Avala.git");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobIsAnnouncedWithItsRepositoryParentAndModel()
    {
        var facts = new CatalogJobFacts(new OneJob(), new Scratch(), Readable());

        Assert.Equal(
            Option<JobAnnouncement>.Some(new JobAnnouncement("remote:github.com/rick/avala", Parent.Value, "opus", "high", Submitted)),
            await facts.AnnouncementAsync(Job, Cancellation));
    }

    [Theory]
    [InlineData("remotes")]
    [InlineData("root commits")]
    public async Task AJobWhoseOriginsCannotBeReadIsNotAnnounced(string unreadable)
    {
        var origins = new Origins(
            unreadable == "remotes" ? WorkspaceFailure.GitFailed : Readable().Remotes,
            unreadable == "root commits" ? WorkspaceFailure.GitFailed : Readable().Roots);
        var facts = new CatalogJobFacts(new OneJob(), new Scratch(), origins);

        Assert.True((await facts.AnnouncementAsync(Job, Cancellation)).IsNone);
    }

    [Fact]
    public async Task AnUnknownJobIsNotAnnounced()
    {
        var facts = new CatalogJobFacts(new OneJob(), new Scratch(), Readable());

        Assert.True((await facts.AnnouncementAsync(JobId.New(), Cancellation)).IsNone);
    }

    [Fact]
    public async Task AJobInAScratchRepositoryIsScratch()
    {
        var facts = new CatalogJobFacts(new OneJob(), new Scratch(Repository), Readable());

        Assert.Equal(Option<bool>.Some(true), await facts.IsScratchAsync(Job, Cancellation));
    }

    private static Origins Readable() => new(
        Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>.Success([Origin]),
        Result<IReadOnlyList<string>, WorkspaceFailure>.Success([]));

    private sealed class OneJob : IJobCatalog
    {
        private static readonly JobSummary Summary = new(Job, Repository, "fix the parser", Submitted, JobStatus.Running, Option<ConnectionName>.None, Option<Autonomy>.None, Option<WorkspaceId>.None)
        {
            Parent = Parent,
            Model = new ModelChoice("opus", "high"),
        };

        public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([Summary]);

        public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(job == Job ? new JobHistory(Summary, [], []) : Option<JobHistory>.None);

        public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([]);

        public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => ValueTask.FromResult(Option<JobTree>.None);
    }

    private sealed class Scratch(params string[] held) : IScratchRepositories
    {
        public string Folder => string.Empty;

        public bool Holds(string repository) => held.Contains(repository);

        public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken) =>
            ValueTask.FromResult<Result<string, WorkspaceFailure>>(WorkspaceFailure.FolderMissing);
    }

    private sealed record Origins(Result<IReadOnlyList<OriginRemote>, WorkspaceFailure> Remotes, Result<IReadOnlyList<string>, WorkspaceFailure> Roots) : IRepositoryOrigins
    {
        public ValueTask<Result<IReadOnlyList<OriginRemote>, WorkspaceFailure>> RemotesAsync(string repository, CancellationToken cancellationToken) => ValueTask.FromResult(Remotes);

        public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> RootCommitsAsync(string repository, CancellationToken cancellationToken) => ValueTask.FromResult(Roots);
    }
}
