using System.Security.Claims;

namespace Avala.Server.Instances.EndPoints.Identity;

internal static class WhoAmIMapper
{
    public static WhoAmIResponse ToResponse(ClaimsPrincipal caller) =>
        new(Guid.Parse(caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty), caller.Identity?.Name ?? string.Empty);
}
