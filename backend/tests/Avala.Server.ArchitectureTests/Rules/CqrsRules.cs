using Ardalis.Result;
using Avala.Server.ArchitectureTests.Layout;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class CqrsRules
{
    private static readonly Type[] MessageKinds = [typeof(ICommand<>), typeof(IQuery<>)];

    private static readonly Type[] HandlerKinds = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];

    private static readonly Type[] HandlerCollaborators =
        [typeof(TimeProvider), typeof(ILogger<>), typeof(IOptions<>), typeof(IOptionsSnapshot<>), typeof(IOptionsMonitor<>)];

    public static IEnumerable<string> MessagesOutsideContracts(ServerScope scope) =>
        Messages(scope).Where(found => found.Owner.Layer != Layer.Contracts).Select(found => Named(found.Type));

    public static IEnumerable<string> MessagesThatAreNotCommandsOrQueries(ServerScope scope) =>
        Messages(scope).Where(found => !MediatorInterfaces(found.Type).Any(MessageKinds.Contains)).Select(found => Named(found.Type));

    public static IEnumerable<string> MessagesAnsweringWithoutAResult(ServerScope scope) =>
        Messages(scope)
            .Where(found => found.Type.GetInterfaces()
                .Any(contract => contract.IsGenericType
                    && MessageKinds.Contains(contract.GetGenericTypeDefinition())
                    && !IsResult(contract.GetGenericArguments()[0])))
            .Select(found => Named(found.Type));

    public static IEnumerable<string> HandlersOutsideInfrastructure(ServerScope scope) =>
        Handlers(scope).Where(found => found.Owner.Layer != Layer.Infrastructure).Select(found => Named(found.Type));

    public static IEnumerable<string> HandlersThatAreNotCommandOrQueryHandlers(ServerScope scope) =>
        Handlers(scope)
            .Where(found => MediatorInterfaces(found.Type).Any(contract => IsHandler(contract) && !HandlerKinds.Contains(contract)))
            .Select(found => Named(found.Type));

    public static IEnumerable<string> HandlerCollaboratorsBesidesTheDbContext(ServerScope scope) =>
        Handlers(scope).SelectMany(found => found.Type.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => !IsAllowedCollaborator(scope, found.Owner, parameter.ParameterType))
            .Select(parameter => $"{found.Type.Name}({parameter.ParameterType.Name})"));

    public static IEnumerable<string> RepositoryAbstractions(ServerScope scope) =>
        scope.Types
            .Where(found => found.Type.Name.Contains("Repository", StringComparison.Ordinal)
                || found.Type.Name.Contains("UnitOfWork", StringComparison.Ordinal))
            .Select(found => Named(found.Type));

    private static IEnumerable<(ModuleAssembly Owner, Type Type)> Messages(ServerScope scope) =>
        scope.Types.Where(found => typeof(IMessage).IsAssignableFrom(found.Type) && !found.Type.IsInterface);

    private static IEnumerable<(ModuleAssembly Owner, Type Type)> Handlers(ServerScope scope) =>
        scope.Types.Where(found => !found.Type.IsInterface && MediatorInterfaces(found.Type).Any(IsHandler));

    private static IEnumerable<Type> MediatorInterfaces(Type type) =>
        type.GetInterfaces()
            .Where(contract => contract.Namespace == typeof(IMessage).Namespace)
            .Select(contract => contract.IsGenericType ? contract.GetGenericTypeDefinition() : contract);

    private static bool IsHandler(Type contract) => contract.Name.Contains("Handler", StringComparison.Ordinal);

    private static bool IsResult(Type type) =>
        type == typeof(Result) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>));

    private static bool IsAllowedCollaborator(ServerScope scope, ModuleAssembly owner, Type parameter) =>
        (parameter.IsSubclassOf(typeof(DbContext)) && scope.Owning(parameter) is { } context && context.Module == owner.Module)
        || HandlerCollaborators.Contains(parameter.IsGenericType ? parameter.GetGenericTypeDefinition() : parameter);

    private static string Named(Type type) => type.FullName ?? type.Name;
}
