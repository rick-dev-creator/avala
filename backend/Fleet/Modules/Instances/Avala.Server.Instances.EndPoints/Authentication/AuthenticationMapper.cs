using System.Security.Claims;
using Avala.Server.Instances.Contracts.Authentication;

namespace Avala.Server.Instances.EndPoints.Authentication;

internal static class AuthenticationMapper
{
    public static AuthenticateInstance ToQuery(string key) => new(key);

    public static ClaimsPrincipal ToPrincipal(AuthenticatedInstance instance) =>
        Principal(
            new Claim(ClaimTypes.NameIdentifier, instance.Id.ToString()),
            new Claim(ClaimTypes.Name, instance.Name),
            new Claim(ClaimTypes.Role, ApiKeyAuthentication.Instance));

    public static ClaimsPrincipal ToAdministrator() =>
        Principal(
            new Claim(ClaimTypes.Name, ApiKeyAuthentication.Administrator),
            new Claim(ClaimTypes.Role, ApiKeyAuthentication.Administrator));

    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, ApiKeyAuthentication.Scheme));
}
