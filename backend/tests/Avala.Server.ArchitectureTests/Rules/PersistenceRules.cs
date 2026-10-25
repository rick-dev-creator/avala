using Avala.Server.ArchitectureTests.Layout;
using Microsoft.EntityFrameworkCore;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class PersistenceRules
{
    private const string DesignConnection = "Host=localhost";

    public static IEnumerable<string> DbContextsOutsideInfrastructure(ServerScope scope) =>
        Contexts(scope).Where(found => found.Owner.Layer != Layer.Infrastructure).Select(found => found.Type.FullName ?? found.Type.Name);

    public static IEnumerable<string> ModulesWithSeveralDbContexts(ServerScope scope) =>
        Contexts(scope).GroupBy(found => found.Owner.Module).Where(module => module.Count() > 1).Select(module => module.Key);

    public static IEnumerable<string> DbContextsOutsideTheirModuleSchema(ServerScope scope) =>
        Contexts(scope)
            .Select(found => (found.Type, Expected: found.Owner.Module.ToLowerInvariant(), Actual: Model(found.Type).GetDefaultSchema()))
            .Where(found => found.Actual != found.Expected)
            .Select(found => $"{found.Type.Name}: {found.Actual ?? "no schema"}");

    public static IEnumerable<string> EntitiesOutsideTheDomain(ServerScope scope) =>
        Contexts(scope).SelectMany(found => Model(found.Type).GetEntityTypes()
            .Where(entity => !entity.HasSharedClrType)
            .Select(entity => entity.ClrType)
            .Where(entity => scope.Owning(entity) is not { Layer: Layer.Domain } owner || owner.Module != found.Owner.Module)
            .Select(entity => $"{found.Type.Name} -> {entity.Name}"));

    private static IEnumerable<(ModuleAssembly Owner, Type Type)> Contexts(ServerScope scope) =>
        scope.Types.Where(found => found.Type.IsSubclassOf(typeof(DbContext)));

    private static Microsoft.EntityFrameworkCore.Metadata.IModel Model(Type contextType)
    {
        var builder = (DbContextOptionsBuilder)Activator.CreateInstance(typeof(DbContextOptionsBuilder<>).MakeGenericType(contextType))!;
        builder.UseNpgsql(DesignConnection);
        using var context = (DbContext)Activator.CreateInstance(contextType, builder.Options)!;

        return context.Model;
    }
}
