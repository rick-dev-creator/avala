using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Source;

internal static class CodeBehindInspector
{
    private static readonly string[] PresentationNamespaces = ["System", "Avalonia"];

    public static IReadOnlyList<string> FindViolations(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var types = root.DescendantNodes().OfType<TypeDeclarationSyntax>().ToList();

        return
        [
            .. types.Skip(1).Select(type => $"declares an extra type {type.Identifier.Text}; move it to its own file or component"),
            .. root.DescendantNodes().OfType<UsingDirectiveSyntax>()
                .Select(directive => directive.NamespaceOrType.ToString())
                .Where(name => !PresentationNamespaces.Any(allowed => name == allowed || name.StartsWith($"{allowed}.", StringComparison.Ordinal)))
                .Select(name => $"uses {name}; code-behind holds presentation only, so it references nothing but System and Avalonia"),
            .. root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
                .Where(constructor => constructor.ParameterList.Parameters.Count > 0)
                .Select(constructor => $"takes dependencies in its constructor at line {LineOf(constructor)}; a view takes none, its view model arrives as DataContext"),
            .. root.DescendantNodes().OfType<IdentifierNameSyntax>()
                .Where(name => name.Identifier.Text.EndsWith("ViewModel", StringComparison.Ordinal))
                .Select(name => $"references {name.Identifier.Text} at line {LineOf(name)}; change the view model's state only through its commands bound in XAML"),
        ];
    }

    private static int LineOf(Microsoft.CodeAnalysis.SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
