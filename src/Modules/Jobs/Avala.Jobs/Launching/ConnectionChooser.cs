using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Launching;

internal sealed class ConnectionChooser(IConnections connections, IEnumerable<IConnectionSelector> selectors, IEventBus bus)
{
    public async Task<Option<ConnectionName>> ChooseAsync(JobId job, string worktree, CancellationToken cancellationToken)
    {
        var catalog = await connections.CatalogAsync(cancellationToken);

        if (catalog.DefaultMode == DefaultMode.Fixed)
        {
            return Option<ConnectionName>.None;
        }

        var chosen = await AskAsync(new ConnectionQuestion(worktree, await CandidatesAsync(catalog, cancellationToken)), cancellationToken);

        return await chosen.Match(
            async choice =>
            {
                await bus.PublishAsync(new ConnectionChosen(job, choice), cancellationToken);

                return Option<ConnectionName>.Some(choice.Connection);
            },
            () => Task.FromResult(Option<ConnectionName>.None));
    }

    public async Task<Option<ConnectionChoice>> PreviewAsync(ConnectionCatalog catalog, string repository, CancellationToken cancellationToken) =>
        await AskAsync(new ConnectionQuestion(repository, await CandidatesAsync(catalog, cancellationToken)) { AtHead = true }, cancellationToken);

    private async Task<Option<ConnectionChoice>> AskAsync(ConnectionQuestion question, CancellationToken cancellationToken)
    {
        if (question.Candidates.Count == 0)
        {
            return Option<ConnectionChoice>.None;
        }

        foreach (var selector in selectors)
        {
            var answer = await selector.ChooseAsync(question, cancellationToken);

            if (answer.IsSome)
            {
                return answer;
            }
        }

        return Option<ConnectionChoice>.None;
    }

    private async Task<IReadOnlyList<ConnectionName>> CandidatesAsync(ConnectionCatalog catalog, CancellationToken cancellationToken)
    {
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
