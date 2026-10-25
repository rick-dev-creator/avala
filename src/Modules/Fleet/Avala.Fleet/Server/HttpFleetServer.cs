using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Sdk;

namespace Avala.Fleet.Server;

internal sealed class HttpFleetServer(FleetHttp http) : IFleetServer
{
    public async Task<SendOutcome> SendAsync(string server, string key, IReadOnlyList<string> bodies, CancellationToken cancellationToken)
    {
        var answer = await http.PostAsync(server, "/activity/v1/events", key, WireJson.Batch(bodies), cancellationToken);

        if (answer.Answer != ServerAnswer.Answered)
        {
            return new SendOutcome(answer.Answer, Option<Delivery.Receipt>.None);
        }

        var receipt = WireJson.ReadReceipt(answer.Body);

        return receipt.Match(found => new SendOutcome(ServerAnswer.Answered, found), () => new SendOutcome(ServerAnswer.Unreachable, Option<Delivery.Receipt>.None));
    }

    public async Task<IdentifyOutcome> IdentifyAsync(string server, string key, CancellationToken cancellationToken)
    {
        var answer = await http.GetAsync(server, "/instances/me", key, cancellationToken);

        return answer.Answer != ServerAnswer.Answered
            ? new IdentifyOutcome(answer.Answer, Option<Identity>.None)
            : WireJson.ReadIdentity(answer.Body).Match(
                identity => new IdentifyOutcome(ServerAnswer.Answered, identity),
                () => new IdentifyOutcome(ServerAnswer.Unreachable, Option<Identity>.None));
    }

    public async Task<FleetView> RepositoriesAsync(string server, string key, CancellationToken cancellationToken)
    {
        var answer = await http.GetAsync(server, "/activity/v1/repositories", key, cancellationToken);

        return answer.Answer switch
        {
            ServerAnswer.Answered => WireJson.ReadRepositories(answer.Body).Match(view => view, () => FleetView.Of(FleetReadState.Unreachable)),
            ServerAnswer.KeyRefused => FleetView.Of(FleetReadState.KeyRefused),
            _ => FleetView.Of(FleetReadState.Unreachable),
        };
    }

    public async Task<FleetJobsView> JobsAsync(string server, string key, string repository, CancellationToken cancellationToken)
    {
        var answer = await http.GetAsync(server, $"/activity/v1/repositories/jobs?key={Uri.EscapeDataString(repository)}", key, cancellationToken);

        return answer.Answer switch
        {
            ServerAnswer.Answered => WireJson.ReadJobs(answer.Body).Match(view => view, () => FleetJobsView.Of(FleetReadState.Unreachable)),
            ServerAnswer.KeyRefused => FleetJobsView.Of(FleetReadState.KeyRefused),
            _ => FleetJobsView.Of(FleetReadState.Unreachable),
        };
    }
}
