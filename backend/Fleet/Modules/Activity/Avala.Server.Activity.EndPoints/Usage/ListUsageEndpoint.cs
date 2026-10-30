using Ardalis.Result;
using FastEndpoints;
using Mediator;
using Microsoft.AspNetCore.Http;

namespace Avala.Server.Activity.EndPoints.Usage;

internal sealed class ListUsageEndpoint(ISender sender) : Endpoint<ListUsageRequest, ListUsageResponse>
{
    public override void Configure()
    {
        Get("/activity/v1/usage");
        Roles(ActivityRoles.Instance, ActivityRoles.Administrator);
        Tags("Activity");
    }

    public override async Task HandleAsync(ListUsageRequest req, CancellationToken ct)
    {
        var result = await sender.Send(ListUsageMapper.ToQuery(req), ct);

        if (result.IsSuccess)
        {
            await Send.OkAsync(ListUsageMapper.ToResponse(result.Value), ct);
            return;
        }

        foreach (var error in result.ValidationErrors)
        {
            AddError(error.ErrorMessage, error.ErrorCode);
        }

        await Send.ErrorsAsync(result.Status == ResultStatus.Invalid ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError, ct);
    }
}
