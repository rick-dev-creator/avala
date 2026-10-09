using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Updates;

namespace Avala.Runtime.Updates;

internal sealed class UpdateCheck(AvalaBuild build, UpdatesFile settings, Option<ReleaseFeed> feed, IEventBus bus) : IUpdates, IStartupTask
{
    private UpdateState latest = UpdateState.NotChecked;

    public UpdateState Latest => Volatile.Read(ref latest);

    public Task Checking { get; private set; } = Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (feed.IsNone)
        {
            return;
        }

        if (await settings.ChecksOnStartupAsync(cancellationToken))
        {
            Checking = Task.Run(() => CheckAsync(cancellationToken), CancellationToken.None);
        }
        else
        {
            Volatile.Write(ref latest, new UpdateState(UpdateStatus.Off, Option<AvailableUpdate>.None));
        }
    }

    public Task<UpdateState> CheckAsync(CancellationToken cancellationToken) =>
        feed.Match(releases => CheckWithAsync(releases, cancellationToken), () => Task.FromResult(Latest));

    private async Task<UpdateState> CheckWithAsync(ReleaseFeed releases, CancellationToken cancellationToken)
    {
        Volatile.Write(ref latest, Latest with { Status = UpdateStatus.Checking });
        var state = (await releases.ReadAsync(cancellationToken)).Match(
            Newest,
            () => new UpdateState(UpdateStatus.Unreachable, Option<AvailableUpdate>.None));
        Volatile.Write(ref latest, state);

        if (state.Update.Match<AvailableUpdate?>(update => update, () => null) is { } found)
        {
            await bus.PublishAsync(new UpdateFound(found), cancellationToken);
        }

        return state;
    }

    private UpdateState Newest(IReadOnlyList<PublishedRelease> releases)
    {
        var current = ReleaseVersion.Parse(build.Version).Match(version => version, () => ReleaseVersion.Zero);
        var newest = releases
            .Where(release => !release.Draft && (current.IsPrerelease || !release.Prerelease) && release.Version.IsNewerThan(current))
            .OrderDescending(Comparer<PublishedRelease>.Create((left, right) => ReleaseVersion.Compare(left.Version, right.Version)))
            .FirstOrDefault();

        return newest is null
            ? new UpdateState(UpdateStatus.UpToDate, Option<AvailableUpdate>.None)
            : new UpdateState(UpdateStatus.Available, new AvailableUpdate(newest.Version.ToString(), newest.Page));
    }
}
