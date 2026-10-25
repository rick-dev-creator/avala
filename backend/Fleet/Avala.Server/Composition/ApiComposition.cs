using Avala.Server.CrossCuttingConcerns.Modules;
using Avala.Server.CrossCuttingConcerns.Validation;
using FastEndpoints;
using FastEndpoints.OpenApi;
using Scalar.AspNetCore;

namespace Avala.Server.Composition;

internal static class ApiComposition
{
    private const string Title = "Avala.Server";

    public static IServiceCollection AddApi(this IServiceCollection services, IReadOnlyList<IModuleInstaller> modules)
    {
        services.AddMediator(options =>
        {
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.GenerateTypesAsInternal = true;
            options.PipelineBehaviors = [typeof(ValidationBehavior<,>)];
        });
        services.AddAuthentication();
        services.AddAuthorization();

        services.AddFastEndpoints(discovery =>
        {
            discovery.DisableAutoDiscovery = true;
            discovery.Assemblies = [.. modules.Select(module => module.Endpoints)];
        }).OpenApiDocument(document =>
        {
            document.DocumentName = "v1";
            document.Title = Title;
            document.Version = "v1";
            document.ShortSchemaNames = true;
        });

        return services;
    }

    public static WebApplication UseApi(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference(scalar => scalar.WithTitle(Title));
        }

        return app;
    }
}
