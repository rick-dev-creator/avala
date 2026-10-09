using Avala.Sdk;

namespace Avala.Verification.Verifying;

internal interface ICheckDeclarations
{
    Task<Option<string>> ReadAsync(string workingDirectory, CancellationToken cancellationToken);
}
