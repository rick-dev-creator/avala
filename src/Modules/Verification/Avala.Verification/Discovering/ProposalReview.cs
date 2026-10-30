using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Verification.Verifying;

namespace Avala.Verification.Discovering;

internal sealed class ProposalReview(JobWorktrees jobs, CheckRunner runner, DiscoveryTerms terms)
{
    public const int MostChecks = 8;

    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(ScoutBrief.LongestCheckSeconds);

    public ValueTask<bool> DiscoversAsync(CancellationToken cancellationToken) => terms.DiscoversAsync(cancellationToken);

    public Task<bool> RunsAsync(string repository, CancellationToken cancellationToken) => terms.RunsAsync(repository, cancellationToken);

    public static string Ran(string ran, string command) => string.Join('\n', [.. Lines(ran), Normal(command)]);

    public static IReadOnlyList<DeclaredCheck> RunBefore(IReadOnlyList<DeclaredCheck> proposed, string ran) =>
        [.. proposed.Where(check => Lines(ran).Contains(Normal(check.CommandLine)))];

    private static string[] Lines(string ran) => ran.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static string Normal(string command) => string.Join(' ', command.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    public static IReadOnlyList<DeclaredCheck> Proposed(string proposal) =>
        [.. CheckDeclaration.Parse(proposal).Match(checks => checks, _ => []).Take(MostChecks).Select(Bounded)];

    public async Task<IReadOnlyList<DeclaredCheck>> ConfirmedAsync(JobId scout, IReadOnlyList<DeclaredCheck> proposed, CancellationToken cancellationToken) =>
        await (await jobs.WorktreeAsync(scout, cancellationToken)).Match(
            worktree => ConfirmedAsync(worktree, proposed, cancellationToken),
            () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]));

    public async Task<IReadOnlyList<DeclaredCheck>> ChosenAsync(Discovery discovery, CancellationToken cancellationToken)
    {
        var scouting = await discovery.Scout.Match(scout => jobs.WorktreeAsync(scout, cancellationToken), () => Task.FromResult(Option<string>.None));
        var worktree = scouting.IsSome ? scouting : await jobs.WorktreeAsync(discovery.Job, cancellationToken);

        return await worktree.Match(
            path => ConfirmedAsync(path, discovery.Checks, cancellationToken),
            () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]));
    }

    private async Task<IReadOnlyList<DeclaredCheck>> ConfirmedAsync(string worktree, IReadOnlyList<DeclaredCheck> proposed, CancellationToken cancellationToken)
    {
        var confirmed = new List<DeclaredCheck>();

        foreach (var check in proposed)
        {
            if ((await runner.RunAsync(check, worktree, cancellationToken)).Status == CheckStatus.Passed)
            {
                confirmed.Add(check);
            }
        }

        return confirmed;
    }

    private static DeclaredCheck Bounded(DeclaredCheck check) => check with { Timeout = check.Timeout > Longest ? Longest : check.Timeout };
}
