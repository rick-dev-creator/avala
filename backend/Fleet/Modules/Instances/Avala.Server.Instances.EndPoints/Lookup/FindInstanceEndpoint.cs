using FastEndpoints;
using Mediator;

namespace Avala.Server.Instances.EndPoints.Lookup;

internal sealed class FindInstanceEndpoint(ISender sender) : Endpoint<FindInstanceRequest, FindInstanceResponse>
{
    public override void Configure()
    {
        Get("/instances/{id}");
        Tags("Instances");
    }

    public override async Task HandleAsync(FindInstanceRequest req, CancellationToken ct)
    {
        var result = await sender.Send(FindInstanceMapper.ToQuery(req), ct);

        if (result.IsSuccess)
        {
            await Send.OkAsync(FindInstanceMapper.ToResponse(result.Value), ct);
            return;
        }

        await Send.NotFoundAsync(ct);
    }
}
