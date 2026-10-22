using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Forges.Git;

internal sealed class GitRemote(IProcessRunner processes) : IGitRemote
{
    private static readonly string[] Making =
    [
        "commit:", "commit (amend):", "commit (initial):", "commit (merge):", "cherry-pick:", "revert:", "am:",
        "rebase (pick):", "rebase (reword):", "rebase (edit):", "rebase (squash):", "rebase (fixup):", "rebase (continue):",
        "rebase -i (pick):", "rebase -i (reword):", "rebase -i (edit):", "rebase -i (squash):", "rebase -i (fixup):", "rebase -i (continue):",
    ];

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

    public async ValueTask<IReadOnlyList<string>> CheckedOutAsync(string worktree, CancellationToken cancellationToken) =>
        (await GitAsync(worktree, ["reflog", "show", "--format=%gs", "HEAD"], cancellationToken)).Match<IReadOnlyList<string>>(
            output => [.. output.ReplaceLineEndings("\n").Split('\n').Select(Moved).OfType<string>().Distinct(StringComparer.Ordinal)],
            _ => []);

    public async ValueTask<bool> PushedAsync(string worktree, string remote, string branch, string commit, CancellationToken cancellationToken) =>
        branch.Length > 0 && commit.Length > 0 && (await GitAsync(worktree, ["reflog", "show", "--format=%H %gs", $"refs/remotes/{remote}/{branch}"], cancellationToken)).Match(
            output => output.ReplaceLineEndings("\n").Split('\n').Any(line => line.StartsWith($"{commit} update by push", StringComparison.Ordinal)),
            _ => false);

    public async ValueTask<bool> HeldAsync(string worktree, string commit, CancellationToken cancellationToken) =>
        commit.Length > 0 && (await GitAsync(worktree, ["reflog", "show", "--format=%H %gs", "HEAD"], cancellationToken)).Match(
            output => output.ReplaceLineEndings("\n").Split('\n').Any(line => line.StartsWith($"{commit} ", StringComparison.Ordinal) && Made(line[(commit.Length + 1)..])),
            _ => false);

    private static bool Made(string subject) =>
        !FastForward(subject)
        && (Making.Any(action => subject.StartsWith(action, StringComparison.Ordinal)) || subject.StartsWith("merge ", StringComparison.Ordinal));

    private static bool FastForward(string subject) =>
        !subject.StartsWith("commit", StringComparison.Ordinal)
        && !subject.StartsWith("revert:", StringComparison.Ordinal)
        && !subject.StartsWith("am:", StringComparison.Ordinal)
        && subject.Contains("fast-forward", StringComparison.OrdinalIgnoreCase);

    private static string? Moved(string subject)
    {
        const string Checkout = "checkout: moving from ";
        var to = subject.LastIndexOf(" to ", StringComparison.Ordinal);

        return subject.StartsWith(Checkout, StringComparison.Ordinal) && to > Checkout.Length ? subject[(to + 4)..].Trim() : null;
    }

    private async Task<Result<string, ForgeError>> GitAsync(string worktree, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        (await processes.RunAsync(new ProcessRequest("git", ["-C", worktree, .. arguments]), cancellationToken)).Match(
            outcome => outcome.Succeeded ? Result<string, ForgeError>.Success(outcome.Output.Trim()) : ForgeError.GitFailed,
            _ => ForgeError.GitFailed);
}
