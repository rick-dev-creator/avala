using Avala.Fleet.Contracts;
using Avala.Fleet.Delivery;
using Avala.Sdk;

namespace Avala.Fleet.Delivering;

internal enum ServerAnswer
{
    Answered,
    BadRequest,
    KeyRefused,
    Unreachable,
}

internal sealed record SendOutcome(ServerAnswer Answer, Option<Receipt> Receipt);

internal sealed record Identity(Guid Id, string Name);

internal sealed record IdentifyOutcome(ServerAnswer Answer, Option<Identity> Identity);

internal interface IFleetServer
{
    Task<SendOutcome> SendAsync(string server, string key, IReadOnlyList<string> bodies, CancellationToken cancellationToken);

    Task<IdentifyOutcome> IdentifyAsync(string server, string key, CancellationToken cancellationToken);

    Task<FleetView> RepositoriesAsync(string server, string key, CancellationToken cancellationToken);

    Task<FleetJobsView> JobsAsync(string server, string key, string repository, CancellationToken cancellationToken);
}
