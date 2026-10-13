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
    Downloading,
    Downloaded,
    DownloadFailed,
}

public sealed record AvailableUpdate(string Version, Uri Release, bool InstallsInPlace = false);

public sealed record UpdateState(UpdateStatus Status, Option<AvailableUpdate> Update)
{
    public static UpdateState NotChecked { get; } = new(UpdateStatus.NotChecked, Option<AvailableUpdate>.None);

    public bool CanDownload => Status is UpdateStatus.Available or UpdateStatus.DownloadFailed && Update.Match(update => update.InstallsInPlace, () => false);

    public bool CanRestart => Status == UpdateStatus.Downloaded;
}

public sealed record UpdateFound(AvailableUpdate Update) : IIntegrationEvent;

public sealed record UpdateDownloaded(AvailableUpdate Update) : IIntegrationEvent;

public sealed record RestartingToUpdate(AvailableUpdate Update) : IIntegrationEvent;

public interface IUpdates
{
    UpdateState Latest { get; }

    Task<UpdateState> CheckAsync(CancellationToken cancellationToken);

    Task<UpdateState> DownloadAsync(CancellationToken cancellationToken);

    Task<UpdateState> RestartAsync(CancellationToken cancellationToken);
}
