using Avala.Server.Activity.Application.RepositoryJobs;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Server.Activity.Application;

internal static class ApplicationServices
{
    public static IServiceCollection AddActivityApplication(this IServiceCollection services) =>
        services.AddValidatorsFromAssemblyContaining<ListRepositoryJobsValidator>(ServiceLifetime.Scoped, includeInternalTypes: true);
}
