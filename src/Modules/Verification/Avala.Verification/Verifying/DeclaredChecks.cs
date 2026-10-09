using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Verifying;

internal sealed class DeclaredChecks(IBaseFiles files) : IRepositoryChecks
{
    public async ValueTask<RepositoryChecks> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        (await files.ReadCurrentAsync(repository, CheckDeclaration.RelativePath, cancellationToken)).Match(
            file => file.Content.Match(
                declaration => CheckDeclaration.Parse(declaration).Match(
                    checks => new RepositoryChecks(ChecksFileStatus.Applied, [.. checks.Select(check => new CheckDeclared(check.Name, check.CommandLine, check.Timeout))], file.Origin),
                    _ => new RepositoryChecks(ChecksFileStatus.Rejected, [], file.Origin)),
                () => new RepositoryChecks(ChecksFileStatus.Absent, [], file.Origin)),
            _ => new RepositoryChecks(ChecksFileStatus.Rejected, [], Option<FileOrigin>.None));
}
