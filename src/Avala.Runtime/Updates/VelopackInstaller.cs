using Avala.Sdk;
using Velopack;
using Velopack.Sources;

namespace Avala.Runtime.Updates;

internal sealed class VelopackInstaller(UpdateManager manager) : IUpdateInstaller
{
    public const string Repository = "https://github.com/rick-dev-creator/avala";

    private UpdateInfo? found;

    public Option<string> Downloaded => manager.UpdatePendingRestart?.Version.ToNormalizedString().ToOption() ?? Option<string>.None;

    public static Option<IUpdateInstaller> ForThisInstall(bool prerelease)
    {
        var manager = new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease));

        return manager.IsInstalled ? Option<IUpdateInstaller>.Some(new VelopackInstaller(manager)) : Option<IUpdateInstaller>.None;
    }

    public async Task<InstallerFinding> FindAsync(CancellationToken cancellationToken)
    {
        try
        {
            var newer = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            Volatile.Write(ref found, newer);

            return new InstallerFinding(true, newer?.TargetFullRelease.Version.ToNormalizedString().ToOption() ?? Option<string>.None);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return InstallerFinding.Unreachable;
        }
    }

    public async Task<bool> DownloadAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref found) is not { } newer)
        {
            return false;
        }

        try
        {
            await manager.DownloadUpdatesAsync(newer, progress: null, cancellationToken);

            return true;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return false;
        }
    }

    public void ApplyOnExit() => manager.WaitExitThenApplyUpdates(manager.UpdatePendingRestart, silent: false, restart: true);
}
