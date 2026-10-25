using Avala.Server.Instances.Application.Registration;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Server.Instances.Application;

internal static class ApplicationServices
{
    public static IServiceCollection AddInstancesApplication(this IServiceCollection services) =>
        services.AddValidatorsFromAssemblyContaining<RegisterInstanceValidator>(ServiceLifetime.Scoped, includeInternalTypes: true);
}
