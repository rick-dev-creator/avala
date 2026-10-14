namespace Avala.Components.Code;

public enum CodeAction
{
    Save,
    Open,
}

public sealed record CodeRequest(CodeAction Action, string Code, string Extension);
