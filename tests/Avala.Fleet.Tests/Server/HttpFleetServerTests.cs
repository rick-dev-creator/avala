using System.Net;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.Server;
using Avala.Testing;

namespace Avala.Fleet.Tests.Server;

public sealed class HttpFleetServerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "KeyRefused")]
    [InlineData(HttpStatusCode.Forbidden, "KeyRefused")]
    [InlineData(HttpStatusCode.BadRequest, "BadRequest")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Unreachable")]
    [InlineData(HttpStatusCode.TooManyRequests, "Unreachable")]
    [InlineData(HttpStatusCode.OK, "Unreachable")]
    public async Task EachAnswerIsReadFromItsStatus(HttpStatusCode status, string expected)
    {
        using var endpoint = new FakeNotifierEndpoint().Replying(status);
        using var http = new FleetHttp(endpoint, owned: false);

        var sent = await new HttpFleetServer(http).SendAsync(Fleets.Server, Fleets.Key, ["{\"a\":1}"], Cancellation);

        Assert.Equal(Enum.Parse<ServerAnswer>(expected), sent.Answer);
    }

    [Fact]
    public async Task ABatchIsPostedWithTheKeyAsABearerToken()
    {
        using var endpoint = new FakeNotifierEndpoint();
        using var http = new FleetHttp(endpoint, owned: false);

        _ = await new HttpFleetServer(http).SendAsync(Fleets.Server, Fleets.Key, ["{\"a\":1}"], Cancellation);

        var request = Assert.Single(endpoint.Received);
        Assert.Equal(
            ("POST", "https://fleet.example/activity/v1/events", $"Bearer {Fleets.Key}", "{\"events\":[{\"a\":1}]}"),
            (request.Method, request.Address.ToString(), request.Headers["Authorization"], request.Body));
    }

    [Fact]
    public async Task AnUnreachableServerIsSaid()
    {
        using var endpoint = new FakeNotifierEndpoint { Unreachable = true };
        using var http = new FleetHttp(endpoint, owned: false);

        Assert.Equal(FleetReadState.Unreachable, (await new HttpFleetServer(http).RepositoriesAsync(Fleets.Server, Fleets.Key, Cancellation)).State);
    }

    [Fact]
    public async Task ARepositorysJobsAreAskedWithItsKeyEscaped()
    {
        using var endpoint = new FakeNotifierEndpoint().Replying(HttpStatusCode.Unauthorized);
        using var http = new FleetHttp(endpoint, owned: false);

        var view = await new HttpFleetServer(http).JobsAsync(Fleets.Server, Fleets.Key, "remote:github.com/rick/avala", Cancellation);

        Assert.Equal(
            (FleetReadState.KeyRefused, "/activity/v1/repositories/jobs?key=remote%3Agithub.com%2Frick%2Favala"),
            (view.State, Assert.Single(endpoint.Received).Address.PathAndQuery));
    }
}
