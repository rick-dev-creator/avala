using System.Reflection;

namespace Avala.ArchitectureTests.Ddd;

internal static class ViewModelRules
{
    public static IEnumerable<string> TakeConcreteDependencies(IEnumerable<Type> types) =>
        types
            .Where(type => type.Name.EndsWith("ViewModel", StringComparison.Ordinal))
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => !parameter.ParameterType.IsInterface))
            .Select(type => type.FullName ?? type.Name);
}
