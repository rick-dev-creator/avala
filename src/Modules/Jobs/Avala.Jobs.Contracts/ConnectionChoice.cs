using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Contracts;

public interface IConnectionSelector
{
    ValueTask<Option<ConnectionChoice>> ChooseAsync(ConnectionQuestion question, CancellationToken cancellationToken);
}

public sealed record ConnectionQuestion(string Worktree, IReadOnlyList<ConnectionName> Candidates);

public enum ChoiceReason
{
    MostCapacity,
    AllAtLimit,
}

public sealed record CandidateCapacity(ConnectionName Connection, double Used, Option<UsageLimit> Window, double Threshold, bool Available);

public sealed record ConnectionChoice(ConnectionName Connection, ChoiceReason Reason, IReadOnlyList<CandidateCapacity> Compared, DateTimeOffset At);

public sealed record ConnectionChosen(JobId Job, ConnectionChoice Choice) : IIntegrationEvent;
