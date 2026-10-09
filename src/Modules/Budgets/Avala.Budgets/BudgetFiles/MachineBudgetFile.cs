using System.Text.Json;
using Avala.Budgets.Admission;
using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.BudgetFiles;

internal sealed class MachineBudgetFile(AvalaPaths paths) : IMachineBudgetFile
{
    public const string FileName = "budgets.json";

    public const int MaximumBytes = 16 * 1024;

    private const string RunningJobs = "runningJobs";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    private Task<MachineBudget>? loading;

    public async ValueTask<MachineBudget> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

    public static Result<Option<int>, BudgetError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Limit(document.RootElement);
        }
        catch (JsonException)
        {
            return BudgetError.Malformed;
        }
    }

    private static Result<Option<int>, BudgetError> Limit(JsonElement root) =>
        root.ValueKind != JsonValueKind.Object ? BudgetError.Malformed
        : root.EnumerateObject().Any(property => property.Name != RunningJobs) ? BudgetError.UnknownField
        : !root.TryGetProperty(RunningJobs, out var limit) ? Option<int>.None
        : RunningJobsLimit(limit);

    private static Result<Option<int>, BudgetError> RunningJobsLimit(JsonElement limit) =>
        limit.ValueKind != JsonValueKind.Number ? BudgetError.Malformed
        : limit.TryGetInt32(out var jobs) && jobs > 0 ? Option<int>.Some(jobs)
        : BudgetError.InvalidRunningJobs;

    private static async Task<MachineBudget> ReadAsync(string path) =>
        !File.Exists(path)
            ? new MachineBudget(Option<int>.None, BudgetFileStatus.Absent, Option<BudgetError>.None)
            : (await ReadTextAsync(path)).Bind(Parse).Match(
                limit => new MachineBudget(limit, BudgetFileStatus.Applied, Option<BudgetError>.None),
                error => new MachineBudget(1, BudgetFileStatus.Rejected, error));

    private static async Task<Result<string, BudgetError>> ReadTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return BudgetError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BudgetError.Unreadable;
        }
    }
}
