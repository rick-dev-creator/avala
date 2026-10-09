using Avala.ArchitectureTests.Scopes;
using IType = ArchUnitNET.Domain.IType;

namespace Avala.ArchitectureTests.Ddd;

internal static class LayerRules
{
    private static readonly Dictionary<Layer, Layer[]> Forbidden = new()
    {
        [Layer.Domain] = [Layer.Application, Layer.Infrastructure, Layer.ViewModels],
        [Layer.Application] = [Layer.Infrastructure, Layer.ViewModels],
        [Layer.Infrastructure] = [Layer.ViewModels],
        [Layer.ViewModels] = [Layer.Domain, Layer.Infrastructure],
        [Layer.Contracts] = [Layer.Domain, Layer.Application, Layer.Infrastructure, Layer.ViewModels],
    };

    public static IEnumerable<string> CrossForbiddenLayers(CodeScope scope) =>
        scope.ArchitectureTypes
            .Where(type => type.NamedTargets.Any(target => Violates(type, target)))
            .Select(type => type.FullName)
            .Distinct();

    public static IEnumerable<string> UndeclaredNamespaces(CodeScope scope) =>
        scope.Types
            .Select(type => type.Namespace ?? string.Empty)
            .Where(ns => !Layers.IsDeclared(ns))
            .Distinct()
            .Order(StringComparer.Ordinal);

    public static IEnumerable<string> ContractsWithLogic(CodeScope scope) =>
        scope.Types
            .Where(type => Layers.Of(type.Namespace) == Layer.Contracts
                && type.IsClass
                && !type.IsRecordClass
                && !typeof(Delegate).IsAssignableFrom(type))
            .Select(type => type.FullName ?? type.Name);

    private static bool Violates(IType origin, IType target)
    {
        var originNamespace = origin.Namespace.FullName;
        var targetNamespace = target.Namespace.FullName;

        return Layers.ModuleOf(originNamespace) == Layers.ModuleOf(targetNamespace)
            && Forbidden.TryGetValue(Layers.Of(originNamespace), out var forbidden)
            && forbidden.Contains(Layers.Of(targetNamespace));
    }
}
