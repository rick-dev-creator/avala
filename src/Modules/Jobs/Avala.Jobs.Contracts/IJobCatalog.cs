using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Contracts;

public interface IJobCatalog
{
    ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken);

    ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken);
}

public enum AttemptOrigin
{
    Initial,
    Retry,
    Hint,
    SendBack,
    Recovery,
}

public enum AttemptOutcome
{
    Running,
    AwaitingCheck,
    Passed,
    Rejected,
    Interrupted,
}

public sealed record JobSummary(
    JobId Job,
    string Repository,
    string Instruction,
    DateTimeOffset Submitted,
    JobStatus Status,
    Option<ConnectionName> Connection,
    Option<Autonomy> Autonomy,
    Option<WorkspaceId> Workspace);

public sealed record AttemptRecord(int Number, AttemptOrigin Origin, AttemptOutcome Outcome, Option<string> Guidance, Option<SessionId> Session);

public sealed record SessionRecord(SessionId Session, IReadOnlyList<int> Attempts);

public sealed record JobHistory(JobSummary Summary, IReadOnlyList<SessionRecord> Sessions, IReadOnlyList<AttemptRecord> Attempts);
