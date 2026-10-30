using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Avala.Server.Instances.EndPoints.Authentication;

internal static class AuthenticationServices
{
    public static IServiceCollection AddInstancesAuthentication(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<AdministratorSettings>, AdministratorSettingsValidator>();
        services.AddAuthentication(ApiKeyAuthentication.Scheme)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthentication.Scheme, configureOptions: null);

        return services;
    }
}
