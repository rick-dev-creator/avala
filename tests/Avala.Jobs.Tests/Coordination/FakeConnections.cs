using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Jobs.Launching;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class FakeConnections : IConnections
{
    public static ProviderInfo Provider { get; } = new("agent", "Agent");

    public Dictionary<string, ConnectionError> Refused { get; } = new(StringComparer.Ordinal);

    public List<DeclaredConnection> Declared { get; } = [];

    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new ConnectionCatalog(
            ConnectionFileStatus.Absent,
            Option<ConnectionError>.None,
            Declared,
            Declared.Count == 0 ? Option<ConnectionName>.None : Declared[0].Name));

    public ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken)
    {
        var name = connection.Match(named => named, () => FakeAgents.DefaultConnection);

        return ValueTask.FromResult(Refused.TryGetValue(name.Value, out var error)
            ? Result<ConnectionInfo, ConnectionError>.Failure(error)
            : Result<ConnectionInfo, ConnectionError>.Success(new ConnectionInfo(name, Provider)));
    }
}

internal sealed class FakeSelector(ConnectionName chosen) : IConnectionSelector
{
    public List<ConnectionQuestion> Questions { get; } = [];

    public ValueTask<Option<ConnectionChoice>> ChooseAsync(ConnectionQuestion question, CancellationToken cancellationToken)
    {
        Questions.Add(question);

        return ValueTask.FromResult(Option<ConnectionChoice>.Some(new ConnectionChoice(
            chosen,
            ChoiceReason.MostCapacity,
            [.. question.Candidates.Select(candidate => new CandidateCapacity(candidate, candidate == chosen ? 0.1 : 0.8, Option<Avala.Agents.Contracts.Events.UsageLimit>.None, 1, true))],
            DateTimeOffset.UnixEpoch)));
    }
}

internal sealed class FakeRepositoryDefaults : IRepositoryDefaults
{
    public Result<Option<ConnectionName>, JobRejection> Connection { get; set; } = Option<ConnectionName>.None;

    public List<string> Read { get; } = [];

    public Result<Option<string>, JobRejection> Approval { get; set; } = Option<string>.None;

    public ValueTask<Result<Option<ConnectionName>, JobRejection>> ConnectionAsync(string worktree, CancellationToken cancellationToken)
    {
        Read.Add(worktree);

        return ValueTask.FromResult(Connection);
    }

    public ValueTask<Result<Option<string>, JobRejection>> ApprovalAsync(string worktree, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Approval);
}
