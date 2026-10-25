using Avala.Fleet.Connecting;
using Avala.Fleet.Connection;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;

namespace Avala.Fleet.Reading;

internal sealed class FleetActivityReader(FleetSession session, IFleetServer server) : IFleetActivity
{
    public async ValueTask<FleetView> RepositoriesAsync(CancellationToken cancellationToken) =>
        await session.Credentials().Match(
            credentials => server.RepositoriesAsync(credentials.Scope.Server, credentials.Key, cancellationToken),
            () => Task.FromResult(FleetView.Of(Unread(session.State))));

    public async ValueTask<FleetJobsView> JobsAsync(string repository, CancellationToken cancellationToken) =>
        await session.Credentials().Match(
            credentials => server.JobsAsync(credentials.Scope.Server, credentials.Key, repository, cancellationToken),
            () => Task.FromResult(FleetJobsView.Of(Unread(session.State))));

    private static FleetReadState Unread(ConnectionState state) => state switch
    {
        ConnectionState.KeyRefused => FleetReadState.KeyRefused,
        ConnectionState.Paused => FleetReadState.Paused,
        _ => FleetReadState.NotConnected,
    };
}
