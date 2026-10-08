using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Source;

internal static class CodeBehindInspector
{
    public static IReadOnlyList<string> FindLogic(string source)
    {
        var types = CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .ToList();

        return
        [
            .. types.Skip(1).Select(type => $"declares an extra type {type.Identifier.Text}"),
            .. types.SelectMany(type => type.Members).Where(member => !IsInitializingConstructor(member))
                .Select(member => $"declares logic at line {member.GetLocation().GetLineSpan().StartLinePosition.Line + 1}"),
        ];
    }

    private static bool IsInitializingConstructor(MemberDeclarationSyntax member) =>
        member is ConstructorDeclarationSyntax { ParameterList.Parameters.Count: 0 } constructor
        && (constructor.ExpressionBody?.Expression ?? SingleStatement(constructor.Body)) is InvocationExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.Text: "InitializeComponent" },
            ArgumentList.Arguments.Count: 0,
        };

    private static ExpressionSyntax? SingleStatement(BlockSyntax? body) =>
        body?.Statements is [ExpressionStatementSyntax statement] ? statement.Expression : null;
}
