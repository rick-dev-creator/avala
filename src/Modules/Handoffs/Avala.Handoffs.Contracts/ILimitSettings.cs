using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Handoffs.Contracts;

public interface ILimitSettings
{
    string MachineFile { get; }

    ValueTask<LimitFile> MachineAsync(CancellationToken cancellationToken);

    ValueTask<Result<LimitFile, HandoffError>> ChangeMachineAsync(LimitSettings settings, CancellationToken cancellationToken);
}

public enum LimitFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public sealed record LimitFile(LimitSettings Settings, LimitFileStatus Status)
{
    public Option<HandoffError> Problem { get; init; }
}

public sealed record LimitSettings(OnLimit OnLimit, double Threshold, double HardCap, IReadOnlyList<ConnectionName> Connections)
{
    public HandoffStrategy Strategy { get; init; } = HandoffStrategy.MostHeadroom;

    public ResumeAtReset Resume { get; init; } = ResumeAtReset.Ask;

    public bool WindDown { get; init; } = true;

    public bool CompactOnResume { get; init; } = true;
}
