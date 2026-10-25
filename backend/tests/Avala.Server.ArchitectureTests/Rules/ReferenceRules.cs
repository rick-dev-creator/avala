using Avala.Server.ArchitectureTests.Layout;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class ReferenceRules
{
    private const string Mediator = "Mediator";

    private const string Results = "Ardalis.Result";

    private const string Injection = "Microsoft.Extensions.DependencyInjection.Abstractions";

    private static readonly string[] InfrastructureExtensions =
    [
        Injection,
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options",
    ];

    public static IEnumerable<string> ReferencesOutsideTheirLayer(ServerScope scope) =>
        scope.Assemblies.SelectMany(owner => owner.Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(reference => !IsRuntime(reference) && !Allows(owner, reference))
            .Select(reference => $"{owner.Name} -> {reference}"));

    private static bool IsRuntime(string reference) =>
        reference is "netstandard" or "mscorlib" or "System" || reference.StartsWith("System.", StringComparison.Ordinal);

    private static bool Allows(ModuleAssembly owner, string reference) => owner.Layer switch
    {
        Layer.Domain => false,
        Layer.Contracts => reference is Mediator or Results,
        Layer.Application => owner.IsOwn(reference, Layer.Contracts, Layer.Domain)
            || reference is Mediator or Results or Injection
            || Starts(reference, "FluentValidation"),
        Layer.Infrastructure => owner.IsOwn(reference, Layer.Contracts, Layer.Domain)
            || reference is Mediator or Results
            || InfrastructureExtensions.Contains(reference)
            || Starts(reference, "Microsoft.EntityFrameworkCore")
            || Starts(reference, "Npgsql"),
        Layer.EndPoints => owner.IsOwn(reference, Layer.Contracts)
            || reference is Mediator or Results or Injection or "Microsoft.Extensions.Primitives"
            || Starts(reference, "FastEndpoints")
            || Starts(reference, "FluentValidation")
            || Starts(reference, "Microsoft.AspNetCore."),
        Layer.Module => owner.IsOwn(reference, Enum.GetValues<Layer>())
            || reference == ServerNames.CrossCuttingConcerns
            || Starts(reference, "Microsoft.Extensions."),
        _ => false,
    };

    private static bool Starts(string reference, string prefix) => reference.StartsWith(prefix, StringComparison.Ordinal);
}
