using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Delegation.Delegating;

internal sealed record Routed(Option<ConnectionName> Connection, Option<ConnectionChoice> Choice);

internal sealed class ConnectionRouter(IEnumerable<IConnectionSelector> selectors)
{
    public async Task<Routed> RouteAsync(DelegationRules rules, int earlierChildren, string worktree, CancellationToken cancellationToken)
    {
        if (rules.Connections.Count == 0 || rules.Routing == Routing.RoundRobin)
        {
            return new Routed(rules.InTurn(earlierChildren), Option<ConnectionChoice>.None);
        }

        foreach (var selector in selectors)
        {
            var answer = await selector.ChooseAsync(new ConnectionQuestion(worktree, rules.Connections), cancellationToken);

            if (answer.IsSome)
            {
                return new Routed(answer.Map(choice => choice.Connection), answer);
            }
        }

        return new Routed(rules.Connections[0], Option<ConnectionChoice>.None);
    }
}
