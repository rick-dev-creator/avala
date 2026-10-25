using Avala.Fleet.Connection;
using Avala.Fleet.Contracts;
using Avala.Fleet.Recording;
using Avala.Sdk;

namespace Avala.Fleet.Connecting;

internal sealed record FleetSettings(string Server, Guid Instance, string InstanceName, bool Enabled, bool KeyRefused)
{
    public FleetScope Scope => new(Server, Instance);
}

internal sealed record FleetCredentials(FleetScope Scope, string Key)
{
    public override string ToString() => $"{nameof(FleetCredentials)} {{ Scope = {Scope.Id}, Key = {KeyShape.Prefix}… }}";
}

internal interface IFleetFile
{
    Task<Option<FleetSettings>> ReadAsync(CancellationToken cancellationToken);

    Task<bool> WriteAsync(FleetSettings settings, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(CancellationToken cancellationToken);
}

internal sealed class FleetSession(IFleetFile file, IKeychain keychain) : IStartupTask, IAsyncDisposable
{
    private readonly SerialExecutor writes = new();

    private Snapshot current = Snapshot.Disconnected;

    public StartupStage Stage => StartupStage.Restore;

    public ChangeSignal Changes { get; } = new();

    public Option<FleetSettings> Settings => Volatile.Read(ref current).Settings;

    public Option<FleetScope> Scope => Settings.Map(found => found.Scope);

    public ConnectionState State => Volatile.Read(ref current).State;

    public bool Records => State == ConnectionState.Connected;

    public Option<FleetCredentials> Credentials() =>
        Volatile.Read(ref current) is { State: ConnectionState.Connected } now ? now.Credentials : Option<FleetCredentials>.None;

    public Option<FleetCredentials> ReadingCredentials() =>
        Volatile.Read(ref current) is { State: ConnectionState.Connected or ConnectionState.Paused } now ? now.Credentials : Option<FleetCredentials>.None;

    public Task RunAsync(CancellationToken cancellationToken) =>
        writes.RunAsync(
            async token =>
            {
                var read = await file.ReadAsync(token);
                Publish(await KeyedAsync(new Snapshot(read, Option<string>.None, KeychainFailed: false), token));
            },
            cancellationToken);

    public Task<bool> ConnectedAsync(FleetSettings connected, string key, CancellationToken cancellationToken) =>
        writes.RunAsync(
            async token =>
            {
                if (!await file.WriteAsync(connected, token))
                {
                    return false;
                }

                Publish(new Snapshot(connected, key, KeychainFailed: false));

                return true;
            },
            cancellationToken);

    public Task<bool> PausedAsync(bool paused, CancellationToken cancellationToken) =>
        ChangedAsync(found => found with { Enabled = !paused }, cancellationToken);

    public Task<bool> RefusedAsync(FleetScope scope, CancellationToken cancellationToken) =>
        ChangedAsync(found => found.Scope == scope ? found with { KeyRefused = true } : found, cancellationToken);

    public Task<bool> RetriedAsync(CancellationToken cancellationToken) =>
        writes.RunAsync(
            token =>
            {
                var now = Volatile.Read(ref current);

                return now.Settings.Match(
                    async found =>
                    {
                        var cleared = found with { KeyRefused = false };

                        if (!await file.WriteAsync(cleared, token))
                        {
                            return false;
                        }

                        Publish(await KeyedAsync(now with { Settings = cleared }, token));

                        return State == ConnectionState.Connected;
                    },
                    () => Task.FromResult(false));
            },
            cancellationToken);

    public Task<bool> ForgottenAsync(CancellationToken cancellationToken) =>
        writes.RunAsync(
            async token =>
            {
                var deleted = await file.DeleteAsync(token);
                Publish(Snapshot.Disconnected);

                return deleted;
            },
            cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private Task<bool> ChangedAsync(Func<FleetSettings, FleetSettings> change, CancellationToken cancellationToken) =>
        writes.RunAsync(
            token =>
            {
                var now = Volatile.Read(ref current);

                return now.Settings.Match(
                    async found =>
                    {
                        var changed = change(found);

                        if (changed == found)
                        {
                            return false;
                        }

                        if (!await file.WriteAsync(changed, token))
                        {
                            return false;
                        }

                        Publish(now with { Settings = changed });

                        return true;
                    },
                    () => Task.FromResult(false));
            },
            cancellationToken);

    private async Task<Snapshot> KeyedAsync(Snapshot snapshot, CancellationToken cancellationToken) =>
        await snapshot.Settings.Match(
            async found => (await keychain.ReadAsync(found.Server, cancellationToken)).Match(
                stored => snapshot with { Key = stored, KeychainFailed = stored.IsNone },
                _ => snapshot with { Key = Option<string>.None, KeychainFailed = true }),
            () => Task.FromResult(snapshot));

    private void Publish(Snapshot snapshot)
    {
        Volatile.Write(ref current, snapshot);
        Changes.Raise();
    }

    private sealed record Snapshot(Option<FleetSettings> Settings, Option<string> Key, bool KeychainFailed)
    {
        public static Snapshot Disconnected { get; } = new(Option<FleetSettings>.None, Option<string>.None, KeychainFailed: false);

        public ConnectionState State =>
            Settings.Match(
                found => found.KeyRefused ? ConnectionState.KeyRefused
                    : KeychainFailed || Key.IsNone ? ConnectionState.KeychainUnavailable
                    : found.Enabled ? ConnectionState.Connected
                    : ConnectionState.Paused,
                () => ConnectionState.NotConnected);

        public Option<FleetCredentials> Credentials =>
            Settings.Bind(found => Key.Map(key => new FleetCredentials(found.Scope, key)));

        public override string ToString() => $"{nameof(Snapshot)} {{ State = {State} }}";
    }
}
