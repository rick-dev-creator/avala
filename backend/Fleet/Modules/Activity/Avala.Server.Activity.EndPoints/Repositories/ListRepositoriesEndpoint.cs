using FastEndpoints;
using Mediator;
using Microsoft.AspNetCore.Http;

namespace Avala.Server.Activity.EndPoints.Repositories;

internal sealed class ListRepositoriesEndpoint(ISender sender) : EndpointWithoutRequest<ListRepositoriesResponse>
{
    public override void Configure()
    {
        Get("/activity/v1/repositories");
        Roles(ActivityRoles.Instance, ActivityRoles.Administrator);
        Tags("Activity");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await sender.Send(ListRepositoriesMapper.ToQuery(), ct);

        if (result.IsSuccess)
        {
            await Send.OkAsync(ListRepositoriesMapper.ToResponse(result.Value), ct);
            return;
        }

        await Send.ErrorsAsync(StatusCodes.Status500InternalServerError, ct);
    }
}
