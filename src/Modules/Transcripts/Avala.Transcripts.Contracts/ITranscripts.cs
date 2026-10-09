using Avala.Agents.Contracts.Events;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Transcripts.Contracts;

public interface ITranscripts
{
    ValueTask<IReadOnlyList<KeptFact>> EarlierRunsAsync(JobId job, CancellationToken cancellationToken);
}

public sealed record KeptFact(Guid Run, DateTimeOffset At, ITranscriptFact Fact);

public interface ITranscriptFact
{
}

public sealed record AttemptBegan(int Attempt) : ITranscriptFact;

public sealed record AgentActed(IAgentEvent Event) : ITranscriptFact;

public sealed record CanvasDrawn(CanvasSnapshot Snapshot) : ITranscriptFact;

public sealed record PermissionRuled(PolicyDecision Decision) : ITranscriptFact;

public sealed record FormRuled(FormDecision Decision) : ITranscriptFact;
