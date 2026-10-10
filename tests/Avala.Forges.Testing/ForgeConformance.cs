using Avala.Forges.Contracts;

namespace Avala.Forges.Testing;

public sealed record ConformanceCase(IForge Forge, ForgeContext Context, string Head, string Base);

public static class ForgeConformance
{
    public static async Task<IReadOnlyList<string>> CheckAsync(ConformanceCase forge, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var (plugin, context, head, target) = forge;

        Check(plugin.Info.Id.Length > 0 && plugin.Info.Name.Length > 0 && plugin.Info.TokenScheme.Length > 0, problems, "the forge declares no id, name or token scheme");

        if ((await plugin.FindAsync(context, head, cancellationToken)).TryGetValue(out var before, out var error))
        {
            Check(before.IsNone, problems, "found a pull request before one was opened");
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
            Check(state.Checks.All(check => check.Name.Length > 0), problems, "a check has no name");
            Check(state.Reviews.All(review => review.Id.Length > 0 && review.Author.Length > 0), problems, "a review has no id or author");
        }
        else
        {
            problems.Add($"reading failed with {error}");
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

    private static void Check(bool holds, List<string> problems, string problem)
    {
        if (!holds)
        {
            problems.Add(problem);
        }
    }
}
