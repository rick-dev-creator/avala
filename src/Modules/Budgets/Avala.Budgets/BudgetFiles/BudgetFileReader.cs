using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Sdk;

namespace Avala.Budgets.BudgetFiles;

internal sealed class BudgetFileReader : IBudgetFiles
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<Result<Option<BudgetCaps>, BudgetError>> ReadAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(workingDirectory, Breaches.BudgetFile);

        if (!File.Exists(path))
        {
            return Option<BudgetCaps>.None;
        }

        return (await ReadTextAsync(path, cancellationToken))
            .Bind(BudgetFileParser.Parse)
            .Map(Option<BudgetCaps>.Some);
    }

    private static async Task<Result<string, BudgetError>> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return BudgetError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BudgetError.Unreadable;
        }
    }
}
