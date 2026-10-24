using Avala.Jobs.Contracts;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Verification.Verifying;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class ProposalReview(JobWorktrees jobs, IWorkingFiles files, CheckRunner runner)
{
    public const int MostChecks = 8;

    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(ScoutBrief.LongestCheckSeconds);

    public async Task<IReadOnlyList<DeclaredCheck>> ConfirmedAsync(JobId scout, CancellationToken cancellationToken) =>
        await (await jobs.WorktreeAsync(scout, cancellationToken)).Match(
            worktree => ConfirmedAsync(worktree, cancellationToken),
            () => Task.FromResult<IReadOnlyList<DeclaredCheck>>([]));

    private async Task<IReadOnlyList<DeclaredCheck>> ConfirmedAsync(string worktree, CancellationToken cancellationToken)
    {
        var proposal = (await files.ReadAsync(worktree, ChecksProposal.Path, cancellationToken)).Match(content => content.Match(text => text, () => string.Empty), _ => string.Empty);
        var proposed = CheckDeclaration.Parse(proposal).Match(checks => checks, _ => []);
        var confirmed = new List<DeclaredCheck>();

        foreach (var check in proposed.Take(MostChecks).Select(Bounded))
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
