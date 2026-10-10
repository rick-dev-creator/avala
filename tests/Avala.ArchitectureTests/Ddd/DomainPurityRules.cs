using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using IType = ArchUnitNET.Domain.IType;

namespace Avala.ArchitectureTests.Ddd;

internal static class DomainPurityRules
{
    private static readonly string[] ForbiddenSystemNamespaces = ["System.IO", "System.Net", "System.Threading.Tasks"];

    private static readonly string[] AllowedAvalaNamespaces = ["Avala.Sdk", "Avala.Sdk.Domain", "Avala.CommandLines"];

    public static IEnumerable<string> DependOnDisallowedTypes(CodeScope scope) =>
        scope.ArchitectureTypes
            .Where(type => Layers.Of(type.Namespace.FullName) == Layer.Domain)
            .Where(type => type.NamedTargets.Any(target =>
                !IsAllowedForDomain(Layers.ModuleOf(type.Namespace.FullName), target)))
            .Select(type => type.FullName)
            .Distinct();

    public static IEnumerable<string> UseStatelessOutsideDomain(CodeScope scope) =>
        scope.ArchitectureTypes
            .Where(type => Layers.Of(type.Namespace.FullName) != Layer.Domain)
            .Where(type => type.NamedTargets.Any(target =>
                target.Namespace.FullName.StartsWith("Stateless", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Distinct();

    public static IEnumerable<string> ReturnForeignErrors(CodeScope scope) =>
        scope.Types
            .Where(type => Layers.Of(type.Namespace) == Layer.Domain)
            .Where(type => type.GetMethods(BuildingBlocks.Declared)
                .Any(method => method.ReturnType.IsResult && IsForeignError(type, method.ReturnType.GetGenericArguments()[1])))
            .Select(type => type.FullName ?? type.Name);

    public static IEnumerable<string> ModulesWithoutASingleErrorEnum(CodeScope scope) =>
        scope.Types
            .Where(type => type.IsAggregate)
            .Select(type => Layers.ModuleOf(type.Namespace))
            .Distinct()
            .Where(module => scope.Types.Count(type =>
                type.IsEnum
                && type.Name.EndsWith("Error", StringComparison.Ordinal)
                && Layers.Of(type.Namespace) == Layer.Domain
                && Layers.ModuleOf(type.Namespace) == module) != 1);

    public static async Task<IReadOnlyList<string>> ThrowAsync(CodeScope scope, CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(
            SolutionLayout.FilesUnder(scope.SourceDirectory, "*.cs"),
            cancellationToken);

        return [.. files.Where(file => Throws(scope, file.Text)).Select(file => Path.GetFileName(file.Path))];
    }

    private static bool Throws(CodeScope scope, string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, cancellationToken: CancellationToken.None).GetRoot();
        var ns = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();

        return scope.Includes(ns)
            && Layers.Of(ns) == Layer.Domain
            && root.DescendantNodes().Any(node => node is ThrowStatementSyntax or ThrowExpressionSyntax);
    }

    private static bool IsAllowedForDomain(string module, IType target)
    {
        var ns = target.Namespace.FullName;

        return ns.Length == 0
            || (ns.StartsWith("System", StringComparison.Ordinal)
                && !ForbiddenSystemNamespaces.Any(forbidden => ns.StartsWith(forbidden, StringComparison.Ordinal))
                && target.FullName != "System.Diagnostics.Process")
            || AllowedAvalaNamespaces.Contains(ns)
            || ns.StartsWith("Stateless", StringComparison.Ordinal)
            || Layers.Of(ns) == Layer.Contracts
            || (Layers.Of(ns) == Layer.Domain && Layers.ModuleOf(ns) == module);
    }

    private static bool IsForeignError(Type owner, Type error) =>
        !error.IsGenericParameter
        && !(Layers.Of(error.Namespace) == Layer.Domain && Layers.ModuleOf(error.Namespace) == Layers.ModuleOf(owner.Namespace));
}
