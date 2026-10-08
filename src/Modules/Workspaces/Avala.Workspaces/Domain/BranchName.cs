using Avala.Sdk;

namespace Avala.Workspaces.Domain;

internal sealed record BranchName
{
    private static readonly string[] Forbidden = ["..", "@{", "//", "~", "^", ":", "?", "*", "[", "\\"];

    private BranchName(string value) => Value = value;

    public string Value { get; }

    public static Result<BranchName, WorkspaceError> Create(string value) =>
        IsValid(value) ? new BranchName(value) : WorkspaceError.InvalidBranchName;

    private static bool IsValid(string value) =>
        !string.IsNullOrEmpty(value)
        && !value.Any(char.IsWhiteSpace)
        && !value.StartsWith('-')
        && !value.StartsWith('/')
        && !value.EndsWith('/')
        && !value.EndsWith('.')
        && !value.EndsWith(".lock", StringComparison.Ordinal)
        && !Forbidden.Any(fragment => value.Contains(fragment, StringComparison.Ordinal));
}
