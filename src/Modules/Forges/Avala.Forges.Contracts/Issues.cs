using Avala.Sdk;

namespace Avala.Forges.Contracts;

public sealed record IssueDraft(string Title, string Body);

public sealed record IssueRef(int Number, Uri Link);

public interface IForgeIssues
{
    ValueTask<Result<IssueRef, ForgeError>> OpenIssueAsync(ForgeContext context, IssueDraft draft, CancellationToken cancellationToken);
}

public interface IIssuePublishing
{
    ValueTask<Result<IssueRef, ForgeError>> PublishAsync(string repository, IssueDraft draft, CancellationToken cancellationToken);
}
