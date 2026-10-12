using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal static class GitRemoteErrors
{
    private static readonly string[] Unauthorized =
    [
        "Authentication failed",
        "could not read Username",
        "could not read Password",
        "terminal prompts disabled",
        "Permission denied",
        "Access denied",
        "returned error: 401",
        "returned error: 403",
        "Host key verification failed",
    ];

    private static readonly string[] Missing =
    [
        "Repository not found",
        "does not appear to be a git repository",
        "does not exist",
        "returned error: 404",
        "not found",
    ];

    private static readonly string[] Unreachable =
    [
        "Could not resolve host",
        "Connection refused",
        "Connection timed out",
        "Network is unreachable",
        "unable to access",
    ];

    public static WorkspaceFailure Classify(string error) =>
        Mentions(error, Unauthorized) ? WorkspaceFailure.RemoteUnauthorized
        : Mentions(error, Missing) ? WorkspaceFailure.RemoteNotFound
        : Mentions(error, Unreachable) ? WorkspaceFailure.RemoteUnreachable
        : Mentions(error, ["[rejected]", "failed to push"]) ? WorkspaceFailure.PushRejected
        : WorkspaceFailure.GitFailed;

    private static bool Mentions(string error, IReadOnlyList<string> phrases) =>
        phrases.Any(phrase => error.Contains(phrase, StringComparison.OrdinalIgnoreCase));
}
