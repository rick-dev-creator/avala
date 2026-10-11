using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Admission;
using Avala.Budgets.Caps;
using Avala.Budgets.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.BudgetFiles;

internal sealed class MachineBudgetFile(AvalaPaths paths) : IMachineBudgetFile
{
    public const string FileName = Ceilings.MachineFile;

    public const int MaximumBytes = 16 * 1024;

    private const string RunningJobs = "runningJobs";
    private const string SpendPerDay = "spendPerDay";
    private const string Connections = "connections";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 4, AllowDuplicateProperties = false };

    private Task<MachineBudget>? loading;

    public async ValueTask<MachineBudget> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

    public static Result<MachineBudget, BudgetError> Parse(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Budget(document.RootElement);
        }
        catch (JsonException)
        {
            return BudgetError.Malformed;
        }
    }

    private static Result<MachineBudget, BudgetError> Budget(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        if (root.EnumerateObject().Any(property => property.Name is not (RunningJobs or SpendPerDay or Connections)))
        {
            return BudgetError.UnknownField;
        }

        if (!Limit(root).TryGetValue(out var limit, out var error)
            || !Spend(root).TryGetValue(out var spend, out error)
            || !ConnectionCeilings(root).TryGetValue(out var ceilings, out error))
        {
            return error;
        }

        return new MachineBudget(limit, BudgetFileStatus.Applied, Option<BudgetError>.None) { SpendPerDay = spend, Connections = ceilings };
    }

    private static Result<Option<int>, BudgetError> Limit(JsonElement root) =>
        !root.TryGetProperty(RunningJobs, out var limit) ? Option<int>.None
        : limit.ValueKind != JsonValueKind.Number ? BudgetError.Malformed
        : limit.TryGetInt32(out var jobs) && jobs > 0 ? Option<int>.Some(jobs)
        : BudgetError.InvalidRunningJobs;

    private static Result<IReadOnlyList<ConnectionCeiling>, BudgetError> ConnectionCeilings(JsonElement root)
    {
        if (!root.TryGetProperty(Connections, out var connections))
        {
            return Result<IReadOnlyList<ConnectionCeiling>, BudgetError>.Success([]);
        }

        if (connections.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        var parsed = new List<ConnectionCeiling>();

        foreach (var connection in connections.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(connection.Name) || connection.Value.ValueKind != JsonValueKind.Object)
            {
                return BudgetError.Malformed;
            }

            if (connection.Value.EnumerateObject().Any(property => property.Name != SpendPerDay))
            {
                return BudgetError.UnknownField;
            }

            if (!Spend(connection.Value).TryGetValue(out var spend, out var error))
            {
                return error;
            }

            parsed.Add(new ConnectionCeiling(new ConnectionName(connection.Name), spend));
        }

        return parsed;
    }

    private static Result<IReadOnlyList<Cost>, BudgetError> Spend(JsonElement root)
    {
        if (!root.TryGetProperty(SpendPerDay, out var ceilings))
        {
            return Result<IReadOnlyList<Cost>, BudgetError>.Success([]);
        }

        if (ceilings.ValueKind != JsonValueKind.Object)
        {
            return BudgetError.Malformed;
        }

        var parsed = new List<Cost>();

        foreach (var ceiling in ceilings.EnumerateObject())
        {
            if (ceiling.Value.ValueKind != JsonValueKind.Number)
            {
                return BudgetError.Malformed;
            }

            if (string.IsNullOrWhiteSpace(ceiling.Name) || !ceiling.Value.TryGetDecimal(out var amount) || amount <= 0)
            {
                return BudgetError.InvalidCost;
            }

            parsed.Add(new Cost(amount, ceiling.Name));
        }

        return parsed;
    }

    private static async Task<MachineBudget> ReadAsync(string path) =>
        !File.Exists(path)
            ? new MachineBudget(Option<int>.None, BudgetFileStatus.Absent, Option<BudgetError>.None)
            : (await ReadTextAsync(path)).Bind(Parse).Match(
                budget => budget,
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
