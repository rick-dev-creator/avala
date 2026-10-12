using Avala.Forges.Contracts;
using Avala.Sdk;

namespace Avala.Forges.Tests.Publishing;

internal sealed class HostingForge : IForge, IForgeRepositories, IForgeAccounts
{
    public const string Id = "hosting";

    public ForgeInfo Info { get; } = new(Id, "Hub");

    public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

    public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();

    public List<RepositoryDraft> Created { get; } = [];

    public string GitHost(Uri api) => api.Host.Replace("api.", string.Empty, StringComparison.Ordinal);

    public ValueTask<Result<HostedRepository, ForgeError>> CreateAsync(ForgeContext context, RepositoryDraft draft, CancellationToken cancellationToken)
    {
        Created.Add(draft);

        return ValueTask.FromResult(Result<HostedRepository, ForgeError>.Success(new HostedRepository(
            new RepositoryAddress("hub.example", "ada", draft.Name),
            new Uri($"https://hub.example/ada/{draft.Name}"),
            $"https://hub.example/ada/{draft.Name}.git",
            $"git@hub.example:ada/{draft.Name}.git")));
    }

    public List<ListedRepository> Listed { get; } = [];

    public Result<ForgeAccount, ForgeError> Me { get; set; } = new ForgeAccount("ada");

    public ValueTask<Result<IReadOnlyList<ListedRepository>, ForgeError>> ListAsync(ForgeContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<IReadOnlyList<ListedRepository>, ForgeError>.Success(Listed));

    public ValueTask<Result<ForgeAccount, ForgeError>> MeAsync(ForgeContext context, CancellationToken cancellationToken) => ValueTask.FromResult(Me);
}

