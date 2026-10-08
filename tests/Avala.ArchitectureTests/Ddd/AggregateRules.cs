using System.Collections.ObjectModel;
using System.Reflection;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Ddd;

internal static class AggregateRules
{
    private static readonly Type[] MutableCollections =
    [
        typeof(List<>), typeof(IList<>), typeof(ICollection<>), typeof(HashSet<>), typeof(ISet<>),
        typeof(Dictionary<,>), typeof(IDictionary<,>), typeof(Collection<>), typeof(ObservableCollection<>),
    ];

    public static IEnumerable<string> ExposeSetters(CodeScope scope) =>
        Aggregates(scope)
            .Where(aggregate => aggregate.GetProperties(BuildingBlocks.Declared)
                .Any(property => property.SetMethod is { IsPrivate: false }))
            .Select(Name);

    public static IEnumerable<string> ExposeMutableCollections(CodeScope scope) =>
        Aggregates(scope)
            .Where(aggregate => aggregate.GetProperties(BuildingBlocks.Declared)
                .Any(property => property.GetMethod is { IsPrivate: false } && IsMutableCollection(property.PropertyType)))
            .Select(Name);

    public static IEnumerable<string> ExposeConstructors(CodeScope scope) =>
        Aggregates(scope)
            .Where(aggregate => aggregate.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(constructor => !constructor.IsPrivate))
            .Select(Name);

    public static IEnumerable<string> HaveOperationsWithoutResult(CodeScope scope) =>
        Aggregates(scope)
            .Where(aggregate => aggregate.GetMethods(BuildingBlocks.Declared)
                .Any(method => method is { IsPublic: true, IsSpecialName: false }
                    && !method.ReturnType.IsResult
                    && method.GetBaseDefinition().DeclaringType != typeof(object)))
            .Select(Name);

    public static IEnumerable<string> ReferenceOtherAggregates(CodeScope scope) =>
        Aggregates(scope)
            .Where(aggregate => aggregate.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(field => Mentions(field.FieldType, type => type.IsAggregate)))
            .Select(Name);

    public static IEnumerable<string> LackStrongIdentifiers(CodeScope scope) =>
        Aggregates(scope)
            .Where(aggregate => aggregate.AggregateIdentifier is not { } identifier
                || !identifier.Name.EndsWith("Id", StringComparison.Ordinal)
                || !identifier.IsRecordStruct
                || !identifier.IsReadOnlyStruct)
            .Select(Name);

    private static IEnumerable<Type> Aggregates(CodeScope scope) => scope.Types.Where(type => type.IsAggregate);

    private static bool IsMutableCollection(Type type) =>
        type.IsArray || (type.IsGenericType && MutableCollections.Contains(type.GetGenericTypeDefinition()));

    private static bool Mentions(Type type, Func<Type, bool> predicate) =>
        predicate(type)
        || (type.GetElementType() is { } element && Mentions(element, predicate))
        || (type.IsGenericType && type.GetGenericArguments().Any(argument => Mentions(argument, predicate)));

    private static string Name(Type type) => type.FullName ?? type.Name;
}
