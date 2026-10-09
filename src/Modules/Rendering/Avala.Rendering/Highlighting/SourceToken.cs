namespace Avala.Rendering.Highlighting;

internal enum TokenKind
{
    Plain,
    Keyword,
    Name,
    Attribute,
    Text,
    Comment,
    Operator,
}

internal sealed record SourceToken(string Text, TokenKind Kind);
