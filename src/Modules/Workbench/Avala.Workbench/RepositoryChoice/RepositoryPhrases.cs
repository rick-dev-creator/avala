using Avala.Workspaces.Contracts;

namespace Avala.Workbench.RepositoryChoice;

internal static class RepositoryPhrases
{
    public const string ChooseTitle = "Choose the repository";

    public const string ParentTitle = "Choose where the new repository goes";

    public const string MakeRepository = "Make it a repository";

    public const string MakeFirstCommit = "Make its first commit";

    public static string Problem(RepositoryState state) => state switch
    {
        RepositoryState.Missing => "There is no folder at this path.",
        RepositoryState.NotARepository => "This folder is not a git repository yet.",
        RepositoryState.NoCommit => "This repository has no commit yet, so a job has nothing to start from.",
        _ => string.Empty,
    };

    public static string Offer(RepositoryState state) => state switch
    {
        RepositoryState.NotARepository => MakeRepository,
        RepositoryState.NoCommit => MakeFirstCommit,
        _ => string.Empty,
    };

    public static string Made(string repository) => $"{Leaf(repository)} is now a git repository with a first commit.";

    public static string Created(string repository) => $"Created {repository}: an empty git repository with a first commit.";

    public static string Failure(WorkspaceFailure failure) => failure switch
    {
        WorkspaceFailure.NoGitIdentity => "Git does not know who you are yet, so it cannot make the first commit: set your name and email with git config --global user.name and git config --global user.email, then try again.",
        WorkspaceFailure.AlreadyARepository => "This folder is already a git repository with commits: Avala leaves its history alone.",
        WorkspaceFailure.FolderMissing => "There is no folder at this path.",
        WorkspaceFailure.FolderExists => "Something with that name is already there: choose another name.",
        WorkspaceFailure.InvalidRepositoryName => "Name the repository with a plain folder name, without slashes.",
        WorkspaceFailure.FileUnwritable => "The folder could not be created there.",
        WorkspaceFailure.InvalidRemoteUrl => "Paste the repository's address, such as https://github.com/owner/name.git or git@github.com:owner/name.git.",
        WorkspaceFailure.RemoteUnauthorized => "Git was refused: set up the credentials git uses for this host, an SSH key or a credential helper such as gh auth setup-git, then try again.",
        WorkspaceFailure.RemoteNotFound => "There is no repository at that address, or your account cannot see it.",
        WorkspaceFailure.RemoteUnreachable => "The host could not be reached: check the address and your connection.",
        _ => "Git could not make the repository.",
    };

    public const string CloneCancelled = "Clone cancelled: nothing was kept.";

    public static string Cloned(RepositoryCloned cloned) => $"Cloned {cloned.Url} into {cloned.Path}.";

    private static string Leaf(string repository) =>
        Path.GetFileName(repository.TrimEnd('/', '\\')) is { Length: > 0 } leaf ? leaf : repository;
}
