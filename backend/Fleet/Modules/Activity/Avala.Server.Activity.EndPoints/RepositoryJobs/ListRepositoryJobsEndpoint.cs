using Ardalis.Result;
using FastEndpoints;
using Mediator;
using Microsoft.AspNetCore.Http;

namespace Avala.Server.Activity.EndPoints.RepositoryJobs;

internal sealed class ListRepositoryJobsEndpoint(ISender sender) : Endpoint<ListRepositoryJobsRequest, ListRepositoryJobsResponse>
{
    public override void Configure()
    {
        Get("/activity/v1/repositories/jobs");
        Roles(ActivityRoles.Instance, ActivityRoles.Administrator);
        Tags("Activity");
    }

    public override async Task HandleAsync(ListRepositoryJobsRequest req, CancellationToken ct)
    {
        var result = await sender.Send(ListRepositoryJobsMapper.ToQuery(req), ct);

        if (result.IsSuccess)
        {
            await Send.OkAsync(ListRepositoryJobsMapper.ToResponse(result.Value), ct);
            return;
        }

        foreach (var error in result.ValidationErrors)
        {
            AddError(error.ErrorMessage, error.ErrorCode);
        }

        await Send.ErrorsAsync(result.Status == ResultStatus.Invalid ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError, ct);
    }
}
