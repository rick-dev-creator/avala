using Avala.Delegation.Escalating;
using Avala.Delegation.Policy;
using Avala.Delegation.RepositoryFiles;
using Avala.Sdk;

namespace Avala.Delegation.MachineFiles;

internal sealed class MachineDelegation(AvalaPaths paths) : IMachineDelegation
{
    public const string FileName = "delegation.json";

    public const int MaximumBytes = 16 * 1024;

    private Task<EscalationTerms>? loading;

    public async ValueTask<EscalationTerms> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

    private static async Task<EscalationTerms> ReadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return EscalationTerms.Undeclared;
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return EscalationTerms.Undeclared;
            }

            using var reader = new StreamReader(stream);

            return DelegationRulesParser.ParseMachine(await reader.ReadToEndAsync(CancellationToken.None)).Match(terms => terms, _ => EscalationTerms.Undeclared);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return EscalationTerms.Undeclared;
        }
    }
}
