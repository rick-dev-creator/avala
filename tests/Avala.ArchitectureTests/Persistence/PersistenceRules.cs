using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.EntityFrameworkCore;

namespace Avala.ArchitectureTests.Persistence;

internal static class PersistenceRules
{
    private const string EntityFrameworkCore = "Microsoft.EntityFrameworkCore";

    public static IEnumerable<string> UseEntityFrameworkOutsideInfrastructure(CodeScope scope) =>
        scope.ArchitectureTypes
            .Where(type => Layers.Of(type.Namespace.FullName) != Layer.Infrastructure)
            .Where(type => type.Dependencies.Any(dependency =>
                dependency.Target.Namespace.FullName == EntityFrameworkCore
                || dependency.Target.Namespace.FullName.StartsWith($"{EntityFrameworkCore}.", StringComparison.Ordinal)))
            .Select(type => type.FullName)
            .Distinct();

    public static async Task<IReadOnlyList<string>> HoldContextsWithoutTaskRunAsync(
        CodeScope scope,
        CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(
            SolutionLayout.FilesUnder(scope.SourceDirectory, "*.cs"),
            cancellationToken);
        var offloading = files
            .Select(file => CSharpSyntaxTree.ParseText(file.Text, cancellationToken: cancellationToken).GetRoot(cancellationToken))
            .Where(InvokesTaskRun)
            .SelectMany(DeclaredTypes)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. scope.Types
                .Where(type => Layers.Of(type.Namespace) == Layer.Infrastructure && HoldsContext(type))
                .Select(type => type.FullName ?? type.Name)
                .Where(name => !offloading.Contains(name)),
        ];
    }

    private static bool HoldsContext(Type type) =>
        type.GetFields(BuildingBlocks.Declared).Any(field => typeof(DbContext).IsAssignableFrom(field.FieldType));

    private static bool InvokesTaskRun(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression is MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: "Task" },
                Name.Identifier.Text: "Run",
            });

    private static IEnumerable<string> DeclaredTypes(SyntaxNode root) =>
        root.DescendantNodes()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .SelectMany(ns => ns.Members.OfType<TypeDeclarationSyntax>().Select(type => $"{ns.Name}.{type.Identifier.Text}"));
}
