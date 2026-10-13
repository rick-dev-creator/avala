using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Updates;

namespace Avala.Runtime.Updates;

internal sealed record UpdateSources(Option<ReleaseFeed> Feed, Option<IUpdateInstaller> Installer)
{
    public static UpdateSources None { get; } = new(Option<ReleaseFeed>.None, Option<IUpdateInstaller>.None);

    public bool Any => Feed.IsSome || Installer.IsSome;
}

internal sealed class UpdateCheck(AvalaBuild build, UpdatesFile settings, UpdateSources sources, IEventBus bus) : IUpdates, IStartupTask
{
    private UpdateState latest = UpdateState.NotChecked;

    public UpdateState Latest => Volatile.Read(ref latest);

    public Task Checking { get; private set; } = Task.CompletedTask;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!sources.Any)
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
        sources.Installer.Match(
            installer => CheckInstalledAsync(installer, cancellationToken),
            () => sources.Feed.Match(releases => CheckWithAsync(releases, cancellationToken), () => Task.FromResult(Latest)));

    public async Task<UpdateState> DownloadAsync(CancellationToken cancellationToken)
    {
        var current = Latest;

        if (!current.CanDownload || sources.Installer.Match<IUpdateInstaller?>(installer => installer, () => null) is not { } installer)
        {
            return current;
        }

        Volatile.Write(ref latest, current with { Status = UpdateStatus.Downloading });
        var downloaded = await installer.DownloadAsync(cancellationToken);
        var state = current with { Status = downloaded ? UpdateStatus.Downloaded : UpdateStatus.DownloadFailed };
        Volatile.Write(ref latest, state);

        if (downloaded)
        {
            await PublishAsync(state, update => new UpdateDownloaded(update), cancellationToken);
        }

        return state;
    }

    public async Task<UpdateState> RestartAsync(CancellationToken cancellationToken)
    {
        var current = Latest;

        if (current.CanRestart && sources.Installer.Match<IUpdateInstaller?>(installer => installer, () => null) is { } installer)
        {
            installer.ApplyOnExit();
            await PublishAsync(current, update => new RestartingToUpdate(update), cancellationToken);
        }

        return current;
    }

    private async Task<UpdateState> CheckInstalledAsync(IUpdateInstaller installer, CancellationToken cancellationToken)
    {
        if (installer.Downloaded.Match<string?>(version => version, () => null) is { } waiting)
        {
            var ready = new UpdateState(UpdateStatus.Downloaded, InPlace(waiting));
            Volatile.Write(ref latest, ready);
            await PublishAsync(ready, update => new UpdateDownloaded(update), cancellationToken);

            return ready;
        }

        Volatile.Write(ref latest, Latest with { Status = UpdateStatus.Checking });
        var finding = await installer.FindAsync(cancellationToken);
        var state = !finding.Reached
            ? new UpdateState(UpdateStatus.Unreachable, Option<AvailableUpdate>.None)
            : finding.Newer.Match(
                version => new UpdateState(UpdateStatus.Available, InPlace(version)),
                () => new UpdateState(UpdateStatus.UpToDate, Option<AvailableUpdate>.None));

        return await SettledAsync(state, cancellationToken);
    }

    private async Task<UpdateState> CheckWithAsync(ReleaseFeed releases, CancellationToken cancellationToken)
    {
        Volatile.Write(ref latest, Latest with { Status = UpdateStatus.Checking });
        var state = (await releases.ReadAsync(cancellationToken)).Match(
            Newest,
            () => new UpdateState(UpdateStatus.Unreachable, Option<AvailableUpdate>.None));

        return await SettledAsync(state, cancellationToken);
    }

    private async Task<UpdateState> SettledAsync(UpdateState state, CancellationToken cancellationToken)
    {
        Volatile.Write(ref latest, state);
        await PublishAsync(state, update => new UpdateFound(update), cancellationToken);

        return state;
    }

    private Task PublishAsync<TEvent>(UpdateState state, Func<AvailableUpdate, TEvent> announce, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent =>
        state.Update.Match<AvailableUpdate?>(update => update, () => null) is { } update
            ? bus.PublishAsync(announce(update), cancellationToken).AsTask()
            : Task.CompletedTask;

    private static AvailableUpdate InPlace(string version) =>
        new(version, new Uri(ReleaseFeed.ReleasesPage, $"releases/tag/v{version}"), InstallsInPlace: true);

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
