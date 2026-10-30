using System.Security.Claims;
using Ardalis.Result;
using Avala.Server.Instances.Contracts.Lookup;
using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.EndPoints.Authentication;
using Avala.Server.Instances.EndPoints.Identity;
using Avala.Server.Instances.EndPoints.Lookup;
using Avala.Server.Instances.EndPoints.Registration;
using Avala.Server.Instances.EndPoints.Revocation;
using Avala.Server.Testing;
using FastEndpoints;

namespace Avala.Server.Instances.Tests.Endpoints;

public sealed class EndpointTests
{
    private static readonly Guid Id = Guid.Parse("0199f6a0-0000-7000-8000-000000000002");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Roles => new()
    {
        { nameof(RegisterInstanceEndpoint), ApiKeyAuthentication.Administrator },
        { nameof(FindInstanceEndpoint), ApiKeyAuthentication.Administrator },
        { nameof(RevokeInstanceEndpoint), ApiKeyAuthentication.Administrator },
        { nameof(WhoAmIEndpoint), ApiKeyAuthentication.Instance },
    };

    [Fact]
    public async Task RegisteringAnswersCreatedWithTheKeyAsync()
    {
        var key = InstanceKey.Issue();
        var endpoint = Factory.Create<RegisterInstanceEndpoint>(
            new FakeSender(_ => Result<RegisteredInstance>.Created(new RegisteredInstance(Id, "desktop", DateTimeOffset.UnixEpoch, key))));

        await endpoint.HandleAsync(new RegisterInstanceRequest("desktop"), Cancellation);

        Assert.Equal((201, key), (endpoint.HttpContext.Response.StatusCode, endpoint.Response.Key));
    }

    [Fact]
    public async Task RegisteringAnInvalidNameIsABadRequestAsync()
    {
        var endpoint = Factory.Create<RegisterInstanceEndpoint>(
            new FakeSender(_ => Result<RegisteredInstance>.Invalid(new ValidationError("Name", "A name is required."))));

        await endpoint.HandleAsync(new RegisterInstanceRequest(string.Empty), Cancellation);

        Assert.Equal(400, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task FindingAnUnknownInstanceIsNotFoundOverHttpAsync()
    {
        var endpoint = Factory.Create<FindInstanceEndpoint>(new FakeSender(_ => Result<InstanceDetails>.NotFound()));

        await endpoint.HandleAsync(new FindInstanceRequest { Id = Id }, Cancellation);

        Assert.Equal(404, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task RevokingAnswersNoContentAsync()
    {
        var endpoint = Factory.Create<RevokeInstanceEndpoint>(new FakeSender(_ => Result.NoContent()));

        await endpoint.HandleAsync(new RevokeInstanceRequest { Id = Id }, Cancellation);

        Assert.Equal(204, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task RevokingAnUnknownInstanceIsNotFoundOverHttpAsync()
    {
        var endpoint = Factory.Create<RevokeInstanceEndpoint>(new FakeSender(_ => Result.NotFound()));

        await endpoint.HandleAsync(new RevokeInstanceRequest { Id = Id }, Cancellation);

        Assert.Equal(404, endpoint.HttpContext.Response.StatusCode);
    }

    [Fact]
    public async Task WhoAmIAnswersFromTheCallersClaimsAsync()
    {
        var endpoint = Factory.Create<WhoAmIEndpoint>(context => context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Id.ToString()), new Claim(ClaimTypes.Name, "laptop")],
            ApiKeyAuthentication.Scheme)));

        await endpoint.HandleAsync(Cancellation);

        Assert.Equal(new WhoAmIResponse(Id, "laptop"), endpoint.Response);
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void EachEndpointDeclaresItsRole(string endpoint, string role) =>
        Assert.Equal([role], Created(endpoint).Definition.AllowedRoles);

    private static BaseEndpoint Created(string endpoint) => endpoint switch
    {
        nameof(RegisterInstanceEndpoint) => Factory.Create<RegisterInstanceEndpoint>(Answering()),
        nameof(FindInstanceEndpoint) => Factory.Create<FindInstanceEndpoint>(Answering()),
        nameof(RevokeInstanceEndpoint) => Factory.Create<RevokeInstanceEndpoint>(Answering()),
        nameof(WhoAmIEndpoint) => Factory.Create<WhoAmIEndpoint>(),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "No such endpoint."),
    };

    private static FakeSender Answering() => new(_ => Result.Success());
}
