using System.Text;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Policy;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.RepositoryFiles;

internal sealed class DelegationRulesReader(IBaseFiles files) : IDelegationRules
{
    public const string JobFile = ".avala/jobs.json";

    public const int MaximumBytes = 16 * 1024;

    public async ValueTask<Result<Option<DelegationRules>, DelegationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(
                text => Encoding.UTF8.GetByteCount(text) > MaximumBytes ? DelegationError.TooLarge : DelegationRulesParser.Parse(text),
                () => Result<Option<DelegationRules>, DelegationError>.Success(Option<DelegationRules>.None)),
            _ => DelegationError.Unreadable);
}
