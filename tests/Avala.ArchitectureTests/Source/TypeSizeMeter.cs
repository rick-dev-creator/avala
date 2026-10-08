using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Source;

internal static class TypeSizeMeter
{
    public static IReadOnlyDictionary<string, int> Measure(IEnumerable<string> sources) =>
        sources
            .SelectMany(source => CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>())
            .GroupBy(QualifiedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(LineCount), StringComparer.Ordinal);

    private static int LineCount(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();

        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static string QualifiedName(BaseTypeDeclarationSyntax declaration) =>
        string.Join('.', declaration.AncestorsAndSelf().Select(Segment).OfType<string>().Reverse());

    private static string? Segment(SyntaxNode node) => node switch
    {
        BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
        TypeDeclarationSyntax { TypeParameterList: { } parameters } type =>
            $"{type.Identifier.Text}`{parameters.Parameters.Count}",
        BaseTypeDeclarationSyntax type => type.Identifier.Text,
        _ => null,
    };
}
