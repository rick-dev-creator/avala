using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

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

    public static IEnumerable<string> DatabasesWithoutMigrations(CodeScope scope) =>
        Contexts(scope)
            .Where(context => !Migrations(scope, context).Any(type => type.IsSubclassOf(typeof(Migration)))
                || !Migrations(scope, context).Any(type => type.IsSubclassOf(typeof(ModelSnapshot))))
            .Select(context => context.FullName ?? context.Name);

    public static IEnumerable<string> DatabasesDriftedFromTheirMigrations(CodeScope scope) =>
        Contexts(scope)
            .Where(context => context.GetConstructor([typeof(string)]) is not null)
            .Where(HasPendingModelChanges)
            .Select(context => context.FullName ?? context.Name);

    private static IEnumerable<Type> Contexts(CodeScope scope) =>
        scope.Types.Where(type => type.IsSubclassOf(typeof(DbContext)));

    private static IEnumerable<Type> Migrations(CodeScope scope, Type context) =>
        scope.Types.Where(type => type.GetCustomAttribute<DbContextAttribute>()?.ContextType == context);

    private static bool HasPendingModelChanges(Type contextType)
    {
        using var context = (DbContext)Activator.CreateInstance(contextType, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.db"))!;

        return context.Database.HasPendingModelChanges();
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
