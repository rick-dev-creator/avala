using System.Reflection;

namespace Avala.ArchitectureTests.Ddd;

internal static class ConstructorRules
{
    public const int MaximumDependencies = 4;

    public static IEnumerable<string> TakeTooManyDependencies(IEnumerable<Type> types) =>
        types
            .Where(type => type.IsClass && !type.IsRecordClass)
            .Where(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(constructor => constructor.GetParameters().Length > MaximumDependencies))
            .Select(type => type.FullName ?? type.Name);
}
