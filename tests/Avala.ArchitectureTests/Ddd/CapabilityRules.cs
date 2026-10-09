using System.Reflection;
using Avala.Agents.Contracts.Capabilities;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Ddd;

internal static class CapabilityRules
{
    public static IEnumerable<string> MisshapenComponents(CodeScope scope) =>
        scope.Types
            .Where(type => !type.IsInterface && typeof(ICapability).IsAssignableFrom(type))
            .Where(type => !type.IsSealed
                || !type.IsRecordClass
                || Layers.Of(type.Namespace) != Layer.Contracts
                || IsMutable(type))
            .Select(type => type.FullName ?? type.Name);

    private static bool IsMutable(Type type) =>
        type.GetProperties(BuildingBlocks.Declared).Any(property => property.SetMethod is { } setter && !BuildingBlocks.IsInitOnly(setter))
        || type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(field => !field.IsInitOnly);
}
