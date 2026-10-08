using Microsoft.CodeAnalysis.CSharp;

namespace Avala.ArchitectureTests.Source;

internal static class CommentFinder
{
    private static readonly SyntaxKind[] CommentKinds =
    [
        SyntaxKind.SingleLineCommentTrivia,
        SyntaxKind.MultiLineCommentTrivia,
        SyntaxKind.SingleLineDocumentationCommentTrivia,
        SyntaxKind.MultiLineDocumentationCommentTrivia,
    ];

    public static IReadOnlyList<int> InCSharp(string source) =>
        [
            .. CSharpSyntaxTree.ParseText(source)
                .GetRoot()
                .DescendantTrivia(descendIntoTrivia: true)
                .Where(trivia => CommentKinds.Contains(trivia.Kind()))
                .Select(trivia => trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1)
                .Distinct(),
        ];

    public static IReadOnlyList<int> InXaml(string source) =>
        [
            .. source.Split('\n')
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => entry.line.Contains("<!--", StringComparison.Ordinal))
                .Select(entry => entry.number),
        ];
}
