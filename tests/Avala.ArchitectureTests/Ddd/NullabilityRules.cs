using System.CodeDom.Compiler;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Ddd;

internal static class NullabilityRules
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] NullBridges = ["Avala.Sdk.Optional"];

    public static IEnumerable<string> ExposeNullables(IEnumerable<Type> types) =>
        types
            .Where(type => !NullBridges.Contains(type.FullName)
                && !type.IsDefined(typeof(CompilerGeneratedAttribute), false)
                && !type.IsDefined(typeof(GeneratedCodeAttribute), false))
            .SelectMany(Violations)
            .Distinct();

    private static IEnumerable<string> Violations(Type type)
    {
        var context = new NullabilityInfoContext();
        var external = ExternalImplementations(type);
        var bindsToView = type.Name.EndsWith("ViewModel", StringComparison.Ordinal);

        var properties = bindsToView
            ? []
            : type.GetProperties(Members)
                .Where(property => IsVisible(property.GetMethod) && !external.Contains(property.GetMethod!))
                .Where(property => IsNullable(context.Create(property)))
                .Select(property => $"{type.FullName}.{property.Name}");

        var methods = type.GetMethods(Members)
            .Where(method => IsVisible(method) && !method.IsSpecialName && !external.Contains(method) && IsOwn(method))
            .Where(method => IsNullable(context.Create(method.ReturnParameter))
                || method.GetParameters().Any(parameter => IsNullable(context.Create(parameter))))
            .Select(method => $"{type.FullName}.{method.Name}");

        var constructors = type.GetConstructors(Members)
            .Where(IsVisible)
            .Where(constructor => constructor.GetParameters().Any(parameter => IsNullable(context.Create(parameter))))
            .Select(_ => $"{type.FullName}.ctor");

        var fields = type.GetFields(Members)
            .Where(field => !field.IsPrivate && !field.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Where(field => IsNullable(context.Create(field)))
            .Select(field => $"{type.FullName}.{field.Name}");

        return [.. properties, .. methods, .. constructors, .. fields];
    }

    private static HashSet<MethodInfo> ExternalImplementations(Type type) =>
        [
            .. type.IsInterface
                ? []
                : type.GetInterfaces()
                    .Where(contract => contract.Namespace is not { } ns || !AvalaAssemblies.IsAvala(ns))
                    .SelectMany(contract => type.GetInterfaceMap(contract).TargetMethods),
        ];

    private static bool IsOwn(MethodInfo method) =>
        !method.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && method.GetBaseDefinition().DeclaringType is { } origin
        && (origin == method.DeclaringType || (origin.Namespace is { } ns && AvalaAssemblies.IsAvala(ns)));

    private static bool IsVisible(MethodBase? member) => member is { IsPrivate: false };

    private static bool IsNullable(NullabilityInfo info) =>
        !(info.Type.IsByRef ? info.Type.GetElementType()! : info.Type).IsGenericParameter
        && info.ReadState == NullabilityState.Nullable
        || Nullable.GetUnderlyingType(info.Type) is not null
        || info.GenericTypeArguments.Any(IsNullable)
        || (info.ElementType is { } element && IsNullable(element));
}
