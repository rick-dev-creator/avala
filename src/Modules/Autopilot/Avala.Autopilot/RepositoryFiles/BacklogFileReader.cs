using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avala.Autopilot.Backlogs;
using Avala.Autopilot.Contracts;
using Avala.Autopilot.Sourcing;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.RepositoryFiles;

internal sealed partial class BacklogFileReader(IBaseFiles files) : IBacklogFile
{
    public const string BacklogFile = ".avala/backlog.json";

    public const int MaximumBytes = 64 * 1024;

    private const double MaximumMinutes = 525_600;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public async Task<Result<BacklogDeclaration, AutopilotError>> CurrentAsync(string repository, CancellationToken cancellationToken) =>
        (await files.ReadCurrentAsync(repository, BacklogFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => BacklogDeclaration.Empty),
            _ => AutopilotError.Unreadable);

    public static Result<BacklogDeclaration, AutopilotError> Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return AutopilotError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Declared(document.RootElement);
        }
        catch (JsonException)
        {
            return AutopilotError.Malformed;
        }
    }

    private static Result<BacklogDeclaration, AutopilotError> Declared(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return AutopilotError.Malformed;
        }

        if (root.EnumerateObject().Any(property => property.Name is not ("tasks" or "recurring")))
        {
            return AutopilotError.UnknownField;
        }

        var declared = List(root, "tasks", ["id", "instruction"], Task)
            .Bind(tasks => List(root, "recurring", ["id", "instruction", "everyMinutes"], Recurring)
                .Map(recurring => new BacklogDeclaration(tasks, recurring)));

        return declared.Bind(backlog =>
            backlog.Tasks.Select(task => task.Key).Concat(backlog.Recurring.Select(task => task.Key)).GroupBy(key => key, StringComparer.Ordinal).Any(keys => keys.Count() > 1)
                ? Result<BacklogDeclaration, AutopilotError>.Failure(AutopilotError.DuplicateKey)
                : backlog);
    }

    private static Result<IReadOnlyList<T>, AutopilotError> List<T>(JsonElement root, string field, string[] fields, Func<JsonElement, Result<T, AutopilotError>> read)
    {
        if (!root.TryGetProperty(field, out var list))
        {
            return Result<IReadOnlyList<T>, AutopilotError>.Success([]);
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            return AutopilotError.Malformed;
        }

        var items = new List<T>();

        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                return AutopilotError.Malformed;
            }

            if (entry.EnumerateObject().Any(property => !fields.Contains(property.Name)))
            {
                return AutopilotError.UnknownField;
            }

            if (!read(entry).TryGetValue(out var parsed, out var error))
            {
                return error;
            }

            items.Add(parsed);
        }

        return items;
    }

    private static Result<BacklogTask, AutopilotError> Task(JsonElement entry) =>
        Key(entry).Bind(key => Instruction(entry).Map(instruction => new BacklogTask(key, instruction)));

    private static Result<RecurringTask, AutopilotError> Recurring(JsonElement entry) =>
        Key(entry).Bind(key => Instruction(entry).Bind(instruction => Every(entry).Map(every => new RecurringTask(key, instruction, every))));

    private static Result<string, AutopilotError> Key(JsonElement entry) =>
        entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { } key && KeyPattern().IsMatch(key)
            ? key
            : AutopilotError.InvalidKey;

    private static Result<string, AutopilotError> Instruction(JsonElement entry)
    {
        if (!entry.TryGetProperty("instruction", out var instruction))
        {
            return AutopilotError.MissingInstruction;
        }

        if (instruction.ValueKind != JsonValueKind.String)
        {
            return AutopilotError.Malformed;
        }

        return instruction.GetString() is { } text && !string.IsNullOrWhiteSpace(text) ? text : AutopilotError.MissingInstruction;
    }

    private static Result<TimeSpan, AutopilotError> Every(JsonElement entry)
    {
        if (!entry.TryGetProperty("everyMinutes", out var every))
        {
            return AutopilotError.InvalidInterval;
        }

        if (every.ValueKind != JsonValueKind.Number)
        {
            return AutopilotError.Malformed;
        }

        return every.GetDouble() is var minutes && minutes > 0 && minutes <= MaximumMinutes
            ? TimeSpan.FromMinutes(minutes)
            : AutopilotError.InvalidInterval;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
