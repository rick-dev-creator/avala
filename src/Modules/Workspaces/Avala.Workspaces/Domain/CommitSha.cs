using Avala.Sdk;

namespace Avala.Workspaces.Domain;

internal sealed record CommitSha
{
    private CommitSha(string value) => Value = value;

    public string Value { get; }

    public static Result<CommitSha, WorkspaceError> Create(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length is 40 or 64 && trimmed.All(char.IsAsciiHexDigitLower)
            ? new CommitSha(trimmed)
            : WorkspaceError.InvalidCommit;
    }
}
