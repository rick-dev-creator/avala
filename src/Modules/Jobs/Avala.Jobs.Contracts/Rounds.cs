using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Jobs.Contracts;

public interface IRoundRouter
{
    ValueTask<RoundRoute> RouteAsync(RoundQuestion question, CancellationToken cancellationToken);
}

public sealed record RoundQuestion(JobId Job, ConnectionName Connection, string Feedback);

public enum RoundAction
{
    GoOn,
    Hold,
    HandOff,
}

public sealed record RoundRoute(RoundAction Action)
{
    public static RoundRoute GoOn { get; } = new(RoundAction.GoOn);

    public HoldReason Hold { get; init; } = HoldReason.LimitNearlyReached;

    public Option<JobHandoff> Handoff { get; init; }

    public static RoundRoute Held(HoldReason reason) => new(RoundAction.Hold) { Hold = reason };

    public static RoundRoute To(JobHandoff handoff) => new(RoundAction.HandOff) { Handoff = handoff };
}

public sealed record JobHandoff(ConnectionChoice Choice, string Brief);

public sealed record JobHandedOff(JobId Job, ConnectionName From, ConnectionName To, SessionId Session, int Attempt, ConnectionChoice Choice) : IIntegrationEvent
{
    public ModelChoice Wanted { get; init; } = ModelChoice.Default;

    public bool ModelFellBack { get; init; }
}
