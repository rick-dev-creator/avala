using Ardalis.Result;
using Avala.Server.Instances.EndPoints.Authentication;
using FastEndpoints;
using Mediator;
using Microsoft.AspNetCore.Http;

namespace Avala.Server.Instances.EndPoints.Registration;

internal sealed class RegisterInstanceEndpoint(ISender sender) : Endpoint<RegisterInstanceRequest, RegisterInstanceResponse>
{
    public override void Configure()
    {
        Post("/instances");
        Roles(ApiKeyAuthentication.Administrator);
        Tags("Instances");
    }

    public override async Task HandleAsync(RegisterInstanceRequest req, CancellationToken ct)
    {
        var result = await sender.Send(RegisterInstanceMapper.ToCommand(req), ct);

        if (result.IsSuccess)
        {
            await Send.ResponseAsync(RegisterInstanceMapper.ToResponse(result.Value), StatusCodes.Status201Created, ct);
            return;
        }

        foreach (var error in result.ValidationErrors)
        {
            AddError(error.ErrorMessage, error.ErrorCode);
        }

        await Send.ErrorsAsync(result.Status == ResultStatus.Invalid ? StatusCodes.Status400BadRequest : StatusCodes.Status500InternalServerError, ct);
    }
}
