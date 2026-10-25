using Avala.Server.Instances.EndPoints.Authentication;
using FastEndpoints;

namespace Avala.Server.Instances.EndPoints.Identity;

internal sealed class WhoAmIEndpoint : EndpointWithoutRequest<WhoAmIResponse>
{
    public override void Configure()
    {
        Get("/instances/me");
        Roles(ApiKeyAuthentication.Instance);
        Tags("Instances");
    }

    public override Task HandleAsync(CancellationToken ct) => Send.OkAsync(WhoAmIMapper.ToResponse(User), ct);
}
