using Avala.Server.CrossCuttingConcerns.Modules;

namespace Avala.Server.Composition;

internal static class ModuleComposition
{
    public static IReadOnlyList<IModuleInstaller> Discover()
    {
        var registry = new ServiceCollection();
        registry.Scan(scan => scan
            .FromApplicationDependencies(assembly => assembly.GetName().Name?.StartsWith("Avala.Server.", StringComparison.Ordinal) == true)
            .AddClasses(classes => classes.AssignableTo<IModuleInstaller>())
            .As<IModuleInstaller>()
            .WithSingletonLifetime());

        return
        [
            .. registry
                .Select(descriptor => descriptor.ImplementationType)
                .OfType<Type>()
                .Select(type => (IModuleInstaller)Activator.CreateInstance(type)!),
        ];
    }

    public static IServiceCollection InstallModules(
        this IServiceCollection services,
        IReadOnlyList<IModuleInstaller> modules,
        IConfiguration configuration)
    {
        foreach (var module in modules)
        {
            module.Install(services, configuration);
        }

        return services;
    }

    public static async Task UseModulesAsync(this IHost host, IReadOnlyList<IModuleInstaller> modules, CancellationToken cancellationToken)
    {
        foreach (var module in modules)
        {
            await module.UseAsync(host, cancellationToken);
        }
    }
}
