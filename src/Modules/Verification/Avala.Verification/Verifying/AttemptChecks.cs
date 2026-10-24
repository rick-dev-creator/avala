using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Discovering;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Verifying;

internal sealed record AttemptDeclaration(Option<FileOrigin> Origin, IReadOnlyList<DeclaredCheck> Checks)
{
    public bool Declared { get; init; } = true;

    public Option<VerificationError> Error { get; init; }

    public bool Discovered { get; init; }

    public bool Shipped { get; init; }
}

internal sealed class AttemptChecks(IBaseFiles files, DiscoveredChecks discovered)
{
    public async Task<AttemptDeclaration> OfAsync(CompletedAttempt attempt, CancellationToken cancellationToken) =>
        await (await files.ReadAsync(attempt.WorkingDirectory, CheckDeclaration.RelativePath, cancellationToken)).Match(
            file => file.Content.Match(
                declaration => Task.FromResult(CheckDeclaration.Parse(declaration).Match(
                    checks => new AttemptDeclaration(file.Origin, checks),
                    error => new AttemptDeclaration(file.Origin, []) { Error = error })),
                () => UndeclaredAsync(attempt, file.Origin, cancellationToken)),
            _ => Task.FromResult(new AttemptDeclaration(Option<FileOrigin>.None, []) { Error = VerificationError.UnreadableDeclaration }));

    private async Task<AttemptDeclaration> UndeclaredAsync(CompletedAttempt attempt, FileOrigin origin, CancellationToken cancellationToken) =>
        (await discovered.OfAsync(attempt, cancellationToken)).Match(
            found => new AttemptDeclaration(origin with { EditedInWorktree = !found.Shipped && origin.EditedInWorktree }, found.Checks) { Discovered = true, Shipped = found.Shipped },
            () => new AttemptDeclaration(origin, []) { Declared = false });
}
