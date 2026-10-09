using Avala.Sdk.Events;

namespace Avala.Sdk.Updates;

public enum UpdateStatus
{
    NotChecked,
    Off,
    Checking,
    UpToDate,
    Available,
    Unreachable,
}

public sealed record AvailableUpdate(string Version, Uri Release);

public sealed record UpdateState(UpdateStatus Status, Option<AvailableUpdate> Update)
{
    public static UpdateState NotChecked { get; } = new(UpdateStatus.NotChecked, Option<AvailableUpdate>.None);
}

public sealed record UpdateFound(AvailableUpdate Update) : IIntegrationEvent;

public interface IUpdates
{
    UpdateState Latest { get; }

    Task<UpdateState> CheckAsync(CancellationToken cancellationToken);
}
