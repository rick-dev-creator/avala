using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Forges.Git;

internal sealed class GitRemote(IProcessRunner processes) : IGitRemote
{
    public async ValueTask<Result<string, ForgeError>> UrlAsync(string worktree, string remote, CancellationToken cancellationToken) =>
        (await GitAsync(worktree, ["remote", "get-url", remote], cancellationToken)).Bind(output => output.Length > 0 ? Result<string, ForgeError>.Success(output) : ForgeError.InvalidRemote);

    public async ValueTask<Result<string, ForgeError>> PushAsync(string worktree, string remote, string branch, CancellationToken cancellationToken)
    {
        var reference = $"refs/heads/{branch}";

        if (!(await GitAsync(worktree, ["push", "--porcelain", "--quiet", remote, $"{reference}:{reference}"], cancellationToken)).TryGetValue(out _, out var error))
        {
            return error == ForgeError.GitFailed ? ForgeError.PushRejected : error;
        }

        return await GitAsync(worktree, ["rev-parse", reference], cancellationToken);
    }

    public async ValueTask<Result<string, ForgeError>> FetchAsync(string worktree, string remote, string branch, CancellationToken cancellationToken) =>
        await GitAsync(worktree, ["fetch", "--quiet", remote, $"refs/heads/{branch}:refs/remotes/{remote}/{branch}"], cancellationToken);

    private async Task<Result<string, ForgeError>> GitAsync(string worktree, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", worktree, .. arguments]), cancellationToken)).Match(
            outcome => outcome.Succeeded ? Result<string, ForgeError>.Success(outcome.Output.Trim()) : ForgeError.GitFailed,
            _ => ForgeError.GitFailed);
}
