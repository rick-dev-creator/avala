using Avala.Delegation.Records;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal sealed class ChildHandoffs(DelegationBook book, IDelegationRules rules) : IHandoffTerms
{
    public async ValueTask<Option<HandoffTerms>> OfAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        await book.RestoreAsync(cancellationToken);

        if (book.OfChild(job).IsNone)
        {
            return Option<HandoffTerms>.None;
        }

        return (await rules.OfWorktreeAsync(worktree, cancellationToken)).Match(
            declared => declared.Bind(section => section.Connections.Count > 0 ? new HandoffTerms(section.Connections) : Option<HandoffTerms>.None),
            _ => Option<HandoffTerms>.None);
    }
}
