using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Launching;

internal sealed class ConnectionChooser(IConnections connections, IEnumerable<IConnectionSelector> selectors, IEventBus bus)
{
    public async Task<Option<ConnectionName>> ChooseAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        var candidates = await CandidatesAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return Option<ConnectionName>.None;
        }

        foreach (var selector in selectors)
        {
            var answer = await selector.ChooseAsync(new ConnectionQuestion(worktree, candidates), cancellationToken);

            if (answer.IsSome)
            {
                return await answer.Match(
                    async choice =>
                    {
                        await bus.PublishAsync(new ConnectionChosen(job, choice), cancellationToken);

                        return Option<ConnectionName>.Some(choice.Connection);
                    },
                    () => Task.FromResult(Option<ConnectionName>.None));
            }
        }

        return Option<ConnectionName>.None;
    }

    private async Task<IReadOnlyList<ConnectionName>> CandidatesAsync(CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);
        var provider = catalog.Default.Bind(named => catalog.Connections.FirstOrDefault(connection => connection.Name == named) is { } found
            ? Option<string>.Some(found.Provider)
            : Option<string>.None);
        var usable = new List<ConnectionName>();

        foreach (var name in catalog.Connections.Where(connection => provider == Option<string>.Some(connection.Provider)).Select(connection => connection.Name))
        {
            if ((await connections.CheckAsync(name, cancellationToken)).IsSuccess)
            {
                usable.Add(name);
            }
        }

        return usable;
    }
}
