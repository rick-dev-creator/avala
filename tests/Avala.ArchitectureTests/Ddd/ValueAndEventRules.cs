using System.Reflection;
using System.Runtime.CompilerServices;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Ddd;

internal static class ValueAndEventRules
{
    public static IEnumerable<string> MutableValueObjects(CodeScope scope) =>
        scope.Types
            .Where(type => Layers.Of(type.Namespace) == Layer.Domain
                && type is { IsValueType: true, IsEnum: false }
                && !type.IsReadOnlyStruct)
            .Select(Name);

    public static IEnumerable<string> MutableEvents(CodeScope scope) =>
        Events(scope)
            .Where(type =>
                type.GetProperties(BuildingBlocks.Declared).Any(property =>
                    property.SetMethod is { } setter && !BuildingBlocks.IsInitOnly(setter))
                || type.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(field => !field.IsInitOnly))
            .Select(Name);

    public static IEnumerable<string> MisplacedDomainEvents(CodeScope scope) =>
        Events(scope)
            .Where(type => type.IsDomainEvent && Layers.Of(type.Namespace) != Layer.Domain)
            .Select(Name);

    public static IEnumerable<string> MisplacedIntegrationEvents(CodeScope scope) =>
        Events(scope)
            .Where(type => type.IsIntegrationEvent && Layers.Of(type.Namespace) != Layer.Contracts)
            .Select(Name);

    public static IEnumerable<string> RecordsPrintingThemselves(CodeScope scope) =>
        scope.Types
            .Where(type => (type.IsRecordClass || type.IsRecordStruct)
                && type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(property => property.PropertyType == type)
                && Generated(type, nameof(ToString))
                && Generated(type, "PrintMembers"))
            .Select(Name);

    private static bool Generated(Type type, string method) =>
        type.GetMethods(BuildingBlocks.Declared).Where(declared => declared.Name == method).All(declared => declared.IsDefined(typeof(CompilerGeneratedAttribute), false));

    private static IEnumerable<Type> Events(CodeScope scope) =>
        scope.Types.Where(type => !type.IsInterface && (type.IsDomainEvent || type.IsIntegrationEvent));

    private static string Name(Type type) => type.FullName ?? type.Name;
}
