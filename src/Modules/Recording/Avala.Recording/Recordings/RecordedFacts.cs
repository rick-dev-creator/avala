using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Recording.Recordings;

internal interface IRecordedFact;

internal interface IHarnessInput : IRecordedFact
{
    int Sequence { get; }
}

internal sealed record Observed(IAgentEvent Event) : IRecordedFact;

internal sealed record FileCaptured(ItemId Item, string Path, string Content) : IRecordedFact;

internal sealed record StreamEnded(bool Crashed) : IRecordedFact;

internal sealed record Stopped : IRecordedFact;

internal sealed record Sent(int Sequence, UserTurn Turn) : IHarnessInput;

internal sealed record Responded(int Sequence, PermissionDecision Decision) : IHarnessInput;

internal sealed record Answered(int Sequence, FormAnswer Answer) : IHarnessInput;

internal sealed record Interrupted(int Sequence) : IHarnessInput;

internal sealed record Refused(int Sequence, AgentError Error) : IRecordedFact;
