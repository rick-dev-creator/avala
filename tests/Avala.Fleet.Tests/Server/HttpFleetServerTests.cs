using System.Net;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.Server;
using Avala.Sdk;
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

        Assert.Equal(FleetReadState.Unreachable, (await new HttpFleetServer(http).UsageAsync(Fleets.Server, Fleets.Key, new FleetUsageQuery(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), FleetStep.Day, Option<string>.None), "UTC", Cancellation)).State);
    }

    [Fact]
    public async Task AnUntrustedCertificateIsSaid()
    {
        using var http = new FleetHttp(new UntrustedHandler(), owned: true);

        var view = await new HttpFleetServer(http).UsageAsync(Fleets.Server, Fleets.Key, new FleetUsageQuery(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), FleetStep.Day, Option<string>.None), "UTC", Cancellation);

        Assert.Equal(FleetReadState.Untrusted, view.State);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, FleetReadState.Outdated)]
    [InlineData(HttpStatusCode.BadRequest, FleetReadState.Refused)]
    public async Task AServerThatCannotAnswerTheUsageSaysWhy(HttpStatusCode status, FleetReadState state)
    {
        using var endpoint = new FakeNotifierEndpoint().Replying(status);
        using var http = new FleetHttp(endpoint, owned: false);

        var view = await new HttpFleetServer(http).UsageAsync(Fleets.Server, Fleets.Key, new FleetUsageQuery(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), FleetStep.Day, Option<string>.None), "UTC", Cancellation);

        Assert.Equal(state, view.State);
    }

    [Fact]
    public async Task AnInstanceIsIdentifiedByItsKey()
    {
        var answering = new AnsweringHandler(await WireJsonTests.GoldenAsync("responses/identity.json"));
        using var http = new FleetHttp(answering, owned: true);

        var identified = await new HttpFleetServer(http).IdentifyAsync(Fleets.Server, Fleets.Key, Cancellation);

        Assert.Equal(
            (ServerAnswer.Answered, "desktop", "https://fleet.example/instances/me", $"Bearer {Fleets.Key}"),
            (identified.Answer, identified.Identity.Match(identity => identity.Name, () => string.Empty), answering.Asked?.ToString(), answering.Authorization));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "KeyRefused")]
    [InlineData(HttpStatusCode.OK, "Unreachable")]
    public async Task AnInstanceThatCannotBeIdentifiedSaysWhy(HttpStatusCode status, string expected)
    {
        using var endpoint = new FakeNotifierEndpoint().Replying(status);
        using var http = new FleetHttp(endpoint, owned: false);

        var identified = await new HttpFleetServer(http).IdentifyAsync(Fleets.Server, Fleets.Key, Cancellation);

        Assert.Equal((Enum.Parse<ServerAnswer>(expected), true), (identified.Answer, identified.Identity.IsNone));
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

internal sealed class AnsweringHandler(string body) : HttpMessageHandler
{
    public Uri? Asked { get; private set; }

    public string? Authorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Asked = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}

internal sealed class UntrustedHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromException<HttpResponseMessage>(new HttpRequestException(
            "The SSL connection could not be established",
            new System.Security.Authentication.AuthenticationException("The remote certificate is invalid because of errors in the certificate chain: UntrustedRoot")));
}
