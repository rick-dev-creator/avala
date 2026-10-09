using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Verifying;

namespace Avala.Verification.FileSystem;

internal sealed class RepositoryDeclarations : ICheckDeclarations
{
    public async Task<Option<string>> ReadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(Path.Combine(workingDirectory, CheckDeclaration.RelativePath), cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Option<string>.None;
        }
    }
}
