using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Discovering;

internal sealed record FoundChecks(IReadOnlyList<DeclaredCheck> Checks, bool Shipped);

internal sealed class DiscoveredChecks(DiscoveryDesk desk, DiscoveryBook book, IWorkingFiles files, IWorkspaces workspaces)
{
    public const string ShippedLabel = "Checks found by Avala";

    public async Task<Option<FoundChecks>> OfAsync(CompletedAttempt attempt, CancellationToken cancellationToken)
    {
        var foundBefore = Found(book.Of(attempt.Job)).IsSome;
        await desk.StartAsync(attempt.Job, cancellationToken);

        return await Found(await book.SettledAsync(attempt.Job, cancellationToken)).Match(
            async discovery => Option<FoundChecks>.Some(await ShippedAsync(attempt.WorkingDirectory, discovery, !foundBefore, cancellationToken)),
            () => Task.FromResult(Option<FoundChecks>.None));
    }

    private static Option<Discovery> Found(Option<Discovery> discovery) => discovery.Bind(found => found.State == DiscoveryState.Found ? found : Option<Discovery>.None);

    private async Task<FoundChecks> ShippedAsync(string worktree, Discovery discovery, bool shippedNow, CancellationToken cancellationToken)
    {
        var shipped = (await files.ReadAsync(worktree, CheckDeclaration.RelativePath, cancellationToken))
            .Match(content => content == Option<string>.Some(discovery.Declaration), _ => false);

        if (shipped && shippedNow)
        {
            await (await workspaces.FindAtAsync(worktree, cancellationToken)).Match(
                async workspace => _ = await workspaces.CheckpointAsync(workspace.Id, ShippedLabel, cancellationToken),
                _ => Task.CompletedTask);
        }

        return new FoundChecks(discovery.Checks, shipped);
    }
}
