using ArchUnitNET.Domain;
using ArchUnitNET.Domain.Dependencies;
using Avala.Server.ArchitectureTests.Layout;
using FastEndpoints;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class EndpointRules
{
    private const string Suffix = "Endpoint";

    private const string MapperSuffix = "Mapper";

    private static readonly Type[] EmptyMessages = [typeof(EmptyRequest), typeof(EmptyResponse)];

    public static IEnumerable<string> EndpointsOutsideEndPoints(ServerScope scope) =>
        Endpoints(scope).Where(found => found.Owner.Layer != Layer.EndPoints).Select(found => Named(found.Type));

    public static IEnumerable<string> EndpointsNotNamedAsEndpoints(ServerScope scope) =>
        Endpoints(scope).Where(found => !found.Type.Name.EndsWith(Suffix, StringComparison.Ordinal)).Select(found => Named(found.Type));

    public static IEnumerable<string> EndpointsExchangingForeignTypes(ServerScope scope) =>
        Endpoints(scope).SelectMany(found => Exchanged(found.Type)
            .Where(exchanged => exchanged.Assembly != found.Type.Assembly && !EmptyMessages.Contains(exchanged))
            .Select(exchanged => $"{found.Type.Name} -> {exchanged.Name}"));

    public static IEnumerable<string> DtosCarryingForeignTypes(ServerScope scope) =>
        Endpoints(scope)
            .SelectMany(found => Exchanged(found.Type).Where(exchanged => exchanged.Assembly == found.Type.Assembly))
            .Distinct()
            .SelectMany(dto => dto.GetProperties()
                .SelectMany(property => Carried(property.PropertyType)
                    .Where(carried => carried.Assembly != dto.Assembly && !IsRuntime(carried))
                    .Select(carried => $"{dto.Name}.{property.Name} -> {carried.Name}")));

    public static IEnumerable<string> EndpointsWithoutAMapper(ServerScope scope) =>
        Endpoints(scope)
            .Where(found => found.Type.Assembly.GetType($"{found.Type.Namespace}.{Feature(found.Type)}{MapperSuffix}") is not { IsAbstract: true, IsSealed: true })
            .Select(found => Named(found.Type));

    public static IEnumerable<string> FeaturesWithSeveralEndpoints(ServerScope scope) =>
        Endpoints(scope).GroupBy(found => found.Type.Namespace).Where(feature => feature.Count() > 1).Select(feature => feature.Key ?? string.Empty);

    public static IEnumerable<string> ContractsBuiltOutsideMappers(ServerScope scope) =>
        scope.Architecture.Types
            .Select(type => (Type: type, Owner: scope.Owning(type.Assembly.FullName)))
            .Where(found => found.Owner is { Layer: Layer.EndPoints } && !found.Type.Name.EndsWith(MapperSuffix, StringComparison.Ordinal))
            .Where(found => found.Type.Dependencies.OfType<MethodCallDependency>().Any(call =>
                call.TargetMember is MethodMember { MethodForm: MethodForm.Constructor }
                && scope.Owning(call.Target.Assembly.FullName) is { Layer: Layer.Contracts }))
            .Select(found => found.Type.FullName)
            .Distinct();

    public static async Task<IReadOnlyList<string>> AnonymousEndpointsAsync(ServerScope scope, CancellationToken cancellationToken)
    {
        List<string> anonymous = [];

        foreach (var path in BackendLayout.FilesUnder(scope.SourceDirectory, "*.cs").Where(IsInEndPoints))
        {
            var root = await CSharpSyntaxTree.ParseText(await File.ReadAllTextAsync(path, cancellationToken), cancellationToken: cancellationToken)
                .GetRootAsync(cancellationToken);

            if (root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(IsAllowAnonymous))
            {
                anonymous.Add(Path.GetFileName(path));
            }
        }

        return anonymous;
    }

    private static IEnumerable<(ModuleAssembly Owner, Type Type)> Endpoints(ServerScope scope) =>
        scope.Types.Where(found => typeof(IEndpoint).IsAssignableFrom(found.Type) && found.Type is { IsClass: true, IsAbstract: false });

    private static IEnumerable<Type> Exchanged(Type endpoint)
    {
        for (var current = endpoint.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.Namespace == typeof(IEndpoint).Namespace)
            {
                foreach (var argument in current.GetGenericArguments())
                {
                    yield return argument;
                }
            }
        }
    }

    private static IEnumerable<Type> Carried(Type type) =>
        type.IsArray ? Carried(type.GetElementType()!)
        : type.IsGenericType ? [type.GetGenericTypeDefinition(), .. type.GetGenericArguments().SelectMany(Carried)]
        : [type];

    private static bool IsRuntime(Type type) =>
        type.Namespace is "System" || type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true;

    private static string Feature(Type endpoint) =>
        endpoint.Name.EndsWith(Suffix, StringComparison.Ordinal) ? endpoint.Name[..^Suffix.Length] : endpoint.Name;

    private static bool IsInEndPoints(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment.EndsWith($".{Layer.EndPoints}", StringComparison.Ordinal));

    private static bool IsAllowAnonymous(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax name => name.Identifier.Text == "AllowAnonymous",
            MemberAccessExpressionSyntax access => access.Name.Identifier.Text == "AllowAnonymous",
            _ => false,
        };

    private static string Named(Type type) => type.FullName ?? type.Name;
}
