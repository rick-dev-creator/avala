using Avala.Forges.Contracts;

namespace Avala.Forges.Testing;

public sealed record ConformanceCase(IForge Forge, ForgeContext Context, string Head, string Base, Func<CancellationToken, Task> OpenFromFork);

public static class ForgeConformance
{
    public static async Task<IReadOnlyList<string>> CheckAsync(ConformanceCase forge, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var (plugin, context, head, target, openFromFork) = forge;

        Check(plugin.Info.Id.Length > 0 && plugin.Info.Name.Length > 0 && plugin.Info.TokenScheme.Length > 0, problems, "the forge declares no id, name or token scheme");

        await openFromFork(cancellationToken);

        if ((await plugin.FindAsync(context, head, cancellationToken)).TryGetValue(out var before, out var error))
        {
            Check(before.IsNone, problems, "found a pull request before one was opened, or one from a fork for the branch of the same name");
        }
        else
        {
            problems.Add($"finding before opening failed with {error}");
        }

        if (!(await plugin.OpenAsync(context, new PullRequestDraft(head, target, "Add a greeting", "Add a greeting\n\nOpened by Avala."), cancellationToken)).TryGetValue(out var opened, out error))
        {
            problems.Add($"opening failed with {error}");

            return problems;
        }

        Check(opened.Number > 0 && opened.Link.IsAbsoluteUri && opened.Head == head && opened.Base == target, problems, $"the opened pull request is not the one asked for: {opened}");

        if ((await plugin.FindAsync(context, head, cancellationToken)).TryGetValue(out var after, out error))
        {
            Check(after.Match(found => found.Number == opened.Number, () => false), problems, "the opened pull request is not found again");
        }
        else
        {
            problems.Add($"finding after opening failed with {error}");
        }

        if ((await plugin.ReadAsync(context, opened, cancellationToken)).TryGetValue(out var state, out error))
        {
            Check(state.Lifecycle == PullRequestLifecycle.Open, problems, "a fresh pull request is not open");
            Check(state.HeadCommit.Length > 0, problems, "the pull request has no head commit");
            Check(state.PullRequest.Number == opened.Number, problems, "reading returned another pull request");
            Check(OwnHead(state), problems, "a pull request opened from the repository's own branch does not name it as its head repository");
            Check(state.Checks.All(check => check.Name.Length > 0), problems, "a check has no name");
            Check(state.Reviews.All(review => review.Id.Length > 0 && review.Author.Length > 0), problems, "a review has no id or author");
        }
        else
        {
            problems.Add($"reading failed with {error}");
        }

        if ((await plugin.ReadAsync(context, opened with { Head = string.Empty, Base = string.Empty }, cancellationToken)).TryGetValue(out var byNumber, out error))
        {
            Check(byNumber.PullRequest.Head == head && byNumber.PullRequest.Base == target, problems, $"reading a pull request by its number alone does not say its head and base: {byNumber.PullRequest}");
        }
        else
        {
            problems.Add($"reading by number failed with {error}");
        }

        if ((await plugin.CommentAsync(context, opened, "Avala checks in.", cancellationToken)).TryGetValue(out var comment, out error))
        {
            Check(comment.Id.Length > 0, problems, "the comment has no id");
        }
        else
        {
            problems.Add($"commenting failed with {error}");
        }

        var unknown = await plugin.ReadAsync(context, opened with { Number = 99 }, cancellationToken);
        Check(unknown.Match(_ => false, failure => failure == ForgeError.NotFound), problems, "an unknown pull request is not NotFound");

        return problems;
    }

    private static bool OwnHead(PullRequestState state) => state.HeadRepository.IsSome && state.HeadRepository == state.BaseRepository;

    public static async Task<IReadOnlyList<string>> CheckAccountAsync(IForge forge, ForgeContext context, CancellationToken cancellationToken)
    {
        var problems = new List<string>();

        if (forge is IForgeAccounts accounts)
        {
            var me = await accounts.MeAsync(context, cancellationToken);
            Check(me.Match(account => account.Login.Length > 0, _ => false), problems, "Me returns no login");
        }

        if (forge is IForgeRepositories hosting)
        {
            if (!(await hosting.CreateAsync(context, new RepositoryDraft("shop", Private: true), cancellationToken)).TryGetValue(out var created, out var error))
            {
                problems.Add($"creating failed with {error}");

                return problems;
            }

            var listed = await hosting.ListAsync(context, cancellationToken);
            Check(listed.Match(found => found.Any(item => item.Repository.Address == created.Address), _ => false), problems, "the repository it created is not listed");
            Check(listed.Match(found => found.Zip(found.Skip(1)).All(pair => pair.First.UpdatedAt >= pair.Second.UpdatedAt), _ => false), problems, "the repositories are not listed most recently updated first");
        }

        return problems;
    }

    private static void Check(bool holds, List<string> problems, string problem)
    {
        if (!holds)
        {
            problems.Add(problem);
        }
    }
}
