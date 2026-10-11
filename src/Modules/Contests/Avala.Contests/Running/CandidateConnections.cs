using Avala.Agents.Contracts.Connections;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Running;

internal sealed class CandidateConnections(IConnections connections, IConnectionPreview preview)
{
    public async Task<IReadOnlyList<Option<ConnectionName>>> RoutesAsync(ContestRequest request, CancellationToken cancellationToken)
    {
        if (request.Job.Connection.IsSome)
        {
            return Same(request.Job.Connection, request.Candidates);
        }

        var usable = request.Spread ? await UsableAsync(cancellationToken) : [];

        if (usable.Count > 0)
        {
            return [.. Enumerable.Range(0, request.Candidates).Select(ordinal => Option<ConnectionName>.Some(usable[ordinal % usable.Count]))];
        }

        var previewed = await preview.PreviewAsync(request.Job.RepositoryPath, cancellationToken);

        return Same(previewed.Match(found => found.Connection, _ => Option<ConnectionName>.None), request.Candidates);
    }

    private async Task<IReadOnlyList<ConnectionName>> UsableAsync(CancellationToken cancellationToken)
    {
        var usable = new List<ConnectionName>();

        foreach (var name in (await connections.CatalogAsync(cancellationToken)).Connections.Select(declared => declared.Name))
        {
            if ((await connections.CheckAsync(name, cancellationToken)).IsSuccess)
            {
                usable.Add(name);
            }
        }

        return usable;
    }

    private static Option<ConnectionName>[] Same(Option<ConnectionName> connection, int candidates) => [.. Enumerable.Repeat(connection, candidates)];
}
