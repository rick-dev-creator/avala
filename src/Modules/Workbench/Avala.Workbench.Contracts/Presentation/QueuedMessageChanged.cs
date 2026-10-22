using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;

namespace Avala.Workbench.Contracts.Presentation;

public enum QueueChange
{
    Queued,
    Taken,
    Withdrawn,
    Returned,
    Dropped,
}

public sealed record QueuedMessageChanged(JobId Job, IReadOnlyList<MessageId> Messages, QueueChange Change);
