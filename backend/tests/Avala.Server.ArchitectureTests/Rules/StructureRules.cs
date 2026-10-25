using Avala.Server.ArchitectureTests.Layout;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class StructureRules
{
    public static IEnumerable<string> ProjectsOutsideTheLayers(ServerScope scope) =>
        scope.ProjectNames.Where(name => !ServerNames.TryParse(name, out _, out _));

    public static IEnumerable<string> MissingLayers(ServerScope scope) =>
        scope.Assemblies
            .GroupBy(found => found.Module)
            .SelectMany(module => Enum.GetValues<Layer>()
                .Where(layer => module.All(found => found.Layer != layer))
                .Select(layer => ServerNames.Project(module.Key, layer)));

    public static IEnumerable<string> TypesOutsideTheirProjectNamespace(ServerScope scope) =>
        scope.Types
            .Where(found => found.Type.Namespace is not { } ns
                || (ns != found.Owner.Name && !ns.StartsWith($"{found.Owner.Name}.", StringComparison.Ordinal)))
            .Select(found => found.Type.FullName ?? found.Type.Name);

    public static IEnumerable<string> ClassesNeitherSealedNorStatic(ServerScope scope) =>
        scope.Types
            .Where(found => found.Type.IsClass && !found.Type.IsSealed)
            .Select(found => found.Type.FullName ?? found.Type.Name);
}
