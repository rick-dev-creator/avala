using Avala.Fleet.Connecting;
using Avala.Fleet.Connection;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;

namespace Avala.Fleet.Reading;

internal sealed class FleetActivityReader(FleetSession session, IFleetServer server, TimeProvider clock) : IFleetActivity
{
    public FleetZone Zone => ZoneOf(clock.LocalTimeZone);

    public async ValueTask<FleetUsageView> UsageAsync(FleetUsageQuery query, CancellationToken cancellationToken) =>
        await session.Credentials().Match(
            credentials => server.UsageAsync(credentials.Scope.Server, credentials.Key, query, Zone.Id, cancellationToken),
            () => Task.FromResult(FleetUsageView.Of(Unread(session.State))));

    public async ValueTask<FleetJobsView> JobsAsync(string repository, CancellationToken cancellationToken) =>
        await session.Credentials().Match(
            credentials => server.JobsAsync(credentials.Scope.Server, credentials.Key, repository, cancellationToken),
            () => Task.FromResult(FleetJobsView.Of(Unread(session.State))));

    internal static FleetZone ZoneOf(TimeZoneInfo zone) =>
        zone.HasIanaId ? new FleetZone(zone.Id, InUtc: false)
        : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? new FleetZone(iana, InUtc: false)
        : new FleetZone(TimeZoneInfo.Utc.Id, InUtc: true);

    private static FleetReadState Unread(ConnectionState state) => state switch
    {
        ConnectionState.KeyRefused => FleetReadState.KeyRefused,
        ConnectionState.Paused => FleetReadState.Paused,
        _ => FleetReadState.NotConnected,
    };
}
