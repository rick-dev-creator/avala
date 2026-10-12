using System.Collections.Immutable;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Git;

internal sealed class GitRemotes(IProcessRunner processes) : IRemoteRepositories
{
    private static readonly ImmutableDictionary<string, string> Unprompted =
        ImmutableDictionary<string, string>.Empty.Add("GIT_TERMINAL_PROMPT", "0");

    public async ValueTask<Option<string>> RemoteAsync(string repository, string remote, CancellationToken cancellationToken)
    {
        var found = await RunAsync(["-C", repository, "remote", "get-url", "--", remote], cancellationToken);

        return found.Succeeded && found.Output.Trim().Length > 0 ? found.Output.Trim() : Option<string>.None;
    }

    public async ValueTask<bool> HasCredentialHelperAsync(string repository, string url, CancellationToken cancellationToken)
    {
        var helper = await RunAsync(["-C", repository, "config", "--get-urlmatch", "credential.helper", url], cancellationToken);

        return helper.Succeeded && helper.Output.Trim().Length > 0;
    }

    public async ValueTask<Result<string, WorkspaceFailure>> PublishAsync(string repository, string remote, string url, CancellationToken cancellationToken)
    {
        if (!IsUrl(url))
        {
            return WorkspaceFailure.InvalidRemoteUrl;
        }

        var branch = await RunAsync(["-C", repository, "symbolic-ref", "--quiet", "--short", "HEAD"], cancellationToken);

        if (!branch.Succeeded || !(await RunAsync(["-C", repository, "rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken)).Succeeded)
        {
            return WorkspaceFailure.NoBaseBranch;
        }

        var name = branch.Output.Trim();
        var known = await RemoteAsync(repository, remote, cancellationToken);

        if (known.Match(existing => existing != url, () => false))
        {
            return WorkspaceFailure.RemoteExists;
        }

        if (known.IsNone && !(await RunAsync(["-C", repository, "remote", "add", "--", remote, url], cancellationToken)).Succeeded)
        {
            return WorkspaceFailure.GitFailed;
        }

        var reference = $"refs/heads/{name}";
        var pushed = await RunAsync(["-C", repository, "push", "--porcelain", "--set-upstream", remote, $"{reference}:{reference}"], cancellationToken);

        return pushed.Succeeded ? name : GitRemoteErrors.Classify($"{pushed.Error}\n{pushed.Output}");
    }

    public async ValueTask<Result<string, WorkspaceFailure>> CloneAsync(string url, string parent, string name, CancellationToken cancellationToken)
    {
        var source = url.Trim();
        var named = name.Trim();

        if (!IsUrl(source))
        {
            return WorkspaceFailure.InvalidRemoteUrl;
        }

        if (!OwnedFolders.IsPlainName(named))
        {
            return WorkspaceFailure.InvalidRepositoryName;
        }

        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent.Trim()))
        {
            return WorkspaceFailure.FolderMissing;
        }

        var path = Path.Combine(Path.GetFullPath(parent.Trim()), named);

        if (Directory.Exists(path) || File.Exists(path))
        {
            return WorkspaceFailure.FolderExists;
        }

        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailure.FileUnwritable;
        }

        return await CloneIntoAsync(source, path, cancellationToken);
    }

    private static bool IsUrl(string url) => url.Length > 0 && !url.StartsWith('-') && !url.Any(char.IsControl);

    private async Task<Result<string, WorkspaceFailure>> CloneIntoAsync(string source, string path, CancellationToken cancellationToken)
    {
        try
        {
            var cloned = await RunAsync(["clone", "--quiet", "--", source, path], cancellationToken);

            if (cloned.Succeeded)
            {
                return path;
            }

            OwnedFolders.Remove(path);

            return GitRemoteErrors.Classify(cloned.Error);
        }
        catch (OperationCanceledException)
        {
            OwnedFolders.Remove(path);
            throw;
        }
    }

    private async Task<(bool Succeeded, string Output, string Error)> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", arguments) { Environment = Unprompted }, cancellationToken))
            .Match(outcome => (outcome.Succeeded, outcome.Output, outcome.Error), error => (false, string.Empty, error.ToString()));
}
