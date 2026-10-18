using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Writing;

internal interface IDocsRules
{
    Task<Result<DocsRules, DocumentationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken);

    Task<Result<DocsRules, DocumentationError>> OfRepositoryAsync(string repository, CancellationToken cancellationToken);

    Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken);

    Task<DocsRulesState> StateAsync(string repository, CancellationToken cancellationToken);

    Task<Result<DocsCommit, DocumentationError>> CommitAsync(string repository, CancellationToken cancellationToken);
}
