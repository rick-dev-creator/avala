using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Escalating;

namespace Avala.Delegation.Delegating;

internal sealed class DelegationOffer(IDelegationRules rules) : IHarnessToolGate
{
    private static readonly HashSet<string> Delegating = new(StringComparer.Ordinal) { DelegationTool.Definition.Name, AnswerChildTool.Definition.Name, WaitChildTool.Definition.Name };

    public async ValueTask<bool> OffersAsync(HarnessTool tool, string workingDirectory, CancellationToken cancellationToken) =>
        !Delegating.Contains(tool.Name)
        || (await rules.OfWorktreeAsync(workingDirectory, cancellationToken)).Match(declared => declared.IsSome, _ => true);
}
