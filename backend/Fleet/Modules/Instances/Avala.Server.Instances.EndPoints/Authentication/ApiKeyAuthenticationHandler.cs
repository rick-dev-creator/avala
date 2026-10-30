using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Avala.Server.Instances.Contracts.Authentication;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avala.Server.Instances.EndPoints.Authentication;

internal sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISender sender,
    IOptions<AdministratorSettings> administrator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;

        if (headers.Count > 1)
        {
            return AuthenticateResult.Fail("Send one Authorization header.");
        }

        if (headers.Count == 0 || !TryReadBearer(headers[0] ?? string.Empty, out var token))
        {
            return AuthenticateResult.NoResult();
        }

        if (token.Length == 0)
        {
            return AuthenticateResult.Fail("The bearer token is empty.");
        }

        if (IsAdministrator(token))
        {
            return Success(AuthenticationMapper.ToAdministrator());
        }

        if (!InstanceKeyShape.Matches(token))
        {
            return AuthenticateResult.Fail("The bearer token is not an instance key.");
        }

        var instance = await sender.Send(AuthenticationMapper.ToQuery(token), Context.RequestAborted);

        return instance.IsSuccess
            ? Success(AuthenticationMapper.ToPrincipal(instance.Value))
            : AuthenticateResult.Fail("The key is unknown or revoked.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = ApiKeyAuthentication.Challenge;

        return Task.CompletedTask;
    }

    private static bool TryReadBearer(string header, out string token)
    {
        var space = header.IndexOf(' ', StringComparison.Ordinal);
        var scheme = space < 0 ? header : header[..space];
        token = space < 0 ? string.Empty : header[(space + 1)..];

        return scheme.Equals(ApiKeyAuthentication.Challenge, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAdministrator(string token) =>
        CryptographicOperations.FixedTimeEquals(Digest(token), Digest(administrator.Value.AdministratorKey));

    private static byte[] Digest(string text) => SHA256.HashData(Encoding.UTF8.GetBytes(text));

    private AuthenticateResult Success(ClaimsPrincipal principal) =>
        AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
}
