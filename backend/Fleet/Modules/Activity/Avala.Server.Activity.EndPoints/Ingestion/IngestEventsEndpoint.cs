using FastEndpoints;
using Mediator;
using Microsoft.AspNetCore.Http;

namespace Avala.Server.Activity.EndPoints.Ingestion;

internal sealed class IngestEventsEndpoint(ISender sender) : Endpoint<IngestEventsRequest, IngestEventsResponse>
{
    public override void Configure()
    {
        Post("/activity/v1/events");
        Roles(ActivityRoles.Instance);
        Tags("Activity");
    }

    public override async Task HandleAsync(IngestEventsRequest req, CancellationToken ct)
    {
        var mapped = IngestEventsMapper.ToCommand(User, req);
        var result = await sender.Send(mapped.Command, ct);

        if (result.IsSuccess)
        {
            await Send.OkAsync(IngestEventsMapper.ToResponse(result.Value, mapped), ct);
            return;
        }

        await Send.ErrorsAsync(StatusCodes.Status500InternalServerError, ct);
    }
}
