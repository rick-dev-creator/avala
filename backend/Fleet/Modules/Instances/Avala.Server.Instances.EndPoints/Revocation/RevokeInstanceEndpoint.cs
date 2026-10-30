using Avala.Server.Instances.EndPoints.Authentication;
using FastEndpoints;
using Mediator;

namespace Avala.Server.Instances.EndPoints.Revocation;

internal sealed class RevokeInstanceEndpoint(ISender sender) : Endpoint<RevokeInstanceRequest, EmptyResponse>
{
    public override void Configure()
    {
        Post("/instances/{id:guid}/revocation");
        Roles(ApiKeyAuthentication.Administrator);
        Tags("Instances");
    }

    public override async Task HandleAsync(RevokeInstanceRequest req, CancellationToken ct)
    {
        var result = await sender.Send(RevokeInstanceMapper.ToCommand(req), ct);

        if (result.IsSuccess)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        await Send.NotFoundAsync(ct);
    }
}
