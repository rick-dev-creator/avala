using Avala.Server.ArchitectureTests.Layout;
using IType = ArchUnitNET.Domain.IType;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class DomainRules
{
    private static readonly string[] ImpureSystemNamespaces =
    [
        "System.ComponentModel.DataAnnotations",
        "System.Data",
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Text.Json",
        "System.Threading.Tasks",
    ];

    public static IEnumerable<string> DependOnMoreThanPureCSharp(ServerScope scope) =>
        scope.Architecture.Types
            .Select(type => (Type: type, Owner: scope.Owning(type.Assembly.FullName)))
            .Where(found => found.Owner is { Layer: Layer.Domain } && !found.Type.Name.Contains('<', StringComparison.Ordinal))
            .Where(found => found.Type.Dependencies.Any(dependency => !IsPure(found.Owner!, dependency.Target)))
            .Select(found => found.Type.FullName)
            .Distinct();

    private static bool IsPure(ModuleAssembly owner, IType target)
    {
        var ns = target.Namespace.FullName;

        return ns.Length == 0
            || ns == owner.Name
            || ns.StartsWith($"{owner.Name}.", StringComparison.Ordinal)
            || ((ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
                && !ImpureSystemNamespaces.Any(impure => ns == impure || ns.StartsWith($"{impure}.", StringComparison.Ordinal)));
    }
}
